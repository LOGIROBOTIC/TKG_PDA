<#
.SYNOPSIS
    Starts the API and the operator client against the EXISTING local database,
    reachable from other devices on the plant network.

.DESCRIPTION
    This is the acceptance launcher: unlike start_local_stack.ps1 it never
    resets, migrates or seeds. It only starts processes, so whatever is already
    in tkg_pda_dev stays exactly as it is.

    Every listener binds 0.0.0.0 instead of 127.0.0.1. Four separate things have
    to line up before a tablet on the same network can actually use the stack,
    and missing any one of them looks like the same symptom:

      1. the API must listen on all interfaces, not just loopback;
      2. the Flutter web server must do the same;
      3. the client must call the API by the address the DEVICE can reach.
         AppConfig already derives that from the browser origin, but only when
         no TKG_API_BASE_URL is compiled in - so this script deliberately does
         NOT pass one. Hardcoding http://127.0.0.1:5000 into the bundle makes a
         phone call itself;
      4. the API's CORS allow-list must contain the LAN origin the browser
         reports. Wildcards are rejected by Program.cs on purpose, so the real
         addresses are enumerated here at start time and survive a DHCP change.

    Windows Firewall still has to admit inbound 5000 and 7357. That needs
    elevation, so it is not done here - the script tells you if a rule is
    missing and prints the command to add it.

    RDS stays fully off: no dispatch, no execution, no discovery.
#>
[CmdletBinding()]
param(
    [int]$ApiPort = 5000,
    [int]$OperatorPort = 7357,
    [int]$PostgresPort = 55450,
    [string]$Database = 'tkg_pda_dev',
    [string]$FlutterExe = 'C:\Users\fatih.cetintas\develop\flutter\bin\flutter.bat',
    [switch]$NoOperator
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$devRoot = Join-Path $env:LOCALAPPDATA 'TKG_PDA_DEV'
$logsDir = Join-Path $devRoot 'logs'
$runtimeDir = Join-Path $projectRoot '.runtime'
foreach ($dir in @($logsDir, $runtimeDir)) {
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}
$runStamp = Get-Date -Format 'yyyyMMdd-HHmmss'

# --- refuse to fight an existing stack -------------------------------------
# Only the ports this run will actually bind: with -NoOperator a client already
# serving on the operator port is left alone rather than blocking the API.
$wantedPorts = @($ApiPort)
if (-not $NoOperator) { $wantedPorts += $OperatorPort }
$busy = $wantedPorts | Where-Object {
    Get-NetTCPConnection -State Listen -LocalPort $_ -ErrorAction SilentlyContinue
}
if ($busy) {
    throw "Port(s) $($busy -join ', ') already in use. Nothing was stopped automatically; run tools\dev\stop_local_stack.ps1 first."
}

$apiDll = Join-Path $projectRoot 'src\TkgPda.Api\bin\Release\net10.0\TkgPda.Api.dll'
if (-not (Test-Path $apiDll)) {
    throw "API build not found at $apiDll. Run: dotnet build TKG.Pda.slnx -c Release"
}

# --- the addresses this machine actually answers on -------------------------
$lanIps = @(Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
    Select-Object -ExpandProperty IPAddress -Unique)
if (-not $lanIps) {
    Write-Warning 'No LAN IPv4 address found. The stack will start but only this machine will reach it.'
}

$corsOrigins = @("http://127.0.0.1:$OperatorPort", "http://localhost:$OperatorPort")
foreach ($ip in $lanIps) { $corsOrigins += "http://${ip}:$OperatorPort" }
$corsOrigins = @($corsOrigins | Select-Object -Unique)

# --- API --------------------------------------------------------------------
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = "http://0.0.0.0:$ApiPort"
$env:ConnectionStrings__TkgPda = "Host=127.0.0.1;Port=$PostgresPort;Database=$Database;Username=postgres;Pooling=true;Maximum Pool Size=50"
# Instance form, not the static GetBytes(int): that overload exists only on
# .NET Core, so the static call fails under Windows PowerShell 5.1.
$keyBytes = New-Object byte[] 48
[Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($keyBytes)
$env:Authentication__Jwt__SigningKey = [Convert]::ToBase64String($keyBytes)
Remove-Variable keyBytes
$env:Rds__Enabled = 'false'
$env:Rds__DispatchEnabled = 'false'
$env:RdsCoreExecution__Enabled = 'false'
$env:RdsCoreDiscovery__Enabled = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Get-ChildItem env:Cors__AllowedOrigins__* -ErrorAction SilentlyContinue | ForEach-Object { Remove-Item "env:$($_.Name)" }
for ($i = 0; $i -lt $corsOrigins.Count; $i++) {
    [Environment]::SetEnvironmentVariable("Cors__AllowedOrigins__$i", $corsOrigins[$i], 'Process')
}

$apiOut = Join-Path $logsDir "api-preserved-$runStamp.stdout.log"
$apiErr = Join-Path $logsDir "api-preserved-$runStamp.stderr.log"
Write-Host "Starting API on http://0.0.0.0:$ApiPort ..." -ForegroundColor Cyan
$api = Start-Process -FilePath 'C:\Program Files\dotnet\dotnet.exe' -ArgumentList @($apiDll) `
    -WorkingDirectory (Join-Path $projectRoot 'src\TkgPda.Api') -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $apiOut -RedirectStandardError $apiErr
$api.Id | Set-Content -LiteralPath (Join-Path $runtimeDir 'api.pid')

# The frontend process must not inherit the signing key or the connection string.
[Environment]::SetEnvironmentVariable('Authentication__Jwt__SigningKey', $null, 'Process')
[Environment]::SetEnvironmentVariable('ConnectionStrings__TkgPda', $null, 'Process')

$ready = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    $api.Refresh()
    if ($api.HasExited) { throw "API exited during startup. Inspect $apiErr" }
    try {
        if ((Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort/health/ready" -TimeoutSec 2).status -eq 'ready') {
            $ready = $true; break
        }
    } catch { }
    Start-Sleep -Milliseconds 400
}
if (-not $ready) { throw "API readiness not confirmed. Operator client not started. Log: $apiErr" }
Write-Host 'API ready.' -ForegroundColor Green

# --- operator client --------------------------------------------------------
$operatorPid = $null
if (-not $NoOperator) {
    if (-not (Test-Path $FlutterExe)) { throw "Flutter not found at $FlutterExe. Pass -FlutterExe." }
    $operatorOut = Join-Path $logsDir "operator-preserved-$runStamp.stdout.log"
    $operatorErr = Join-Path $logsDir "operator-preserved-$runStamp.stderr.log"
    # No --dart-define here: AppConfig then resolves the API from the browser
    # origin, so each device calls the host it actually loaded the page from.
    $flutterArgs = "`"$FlutterExe`" run -d web-server --web-hostname=0.0.0.0 --web-port=$OperatorPort"
    Write-Host "Starting operator client on http://0.0.0.0:$OperatorPort ..." -ForegroundColor Cyan
    $operator = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', $flutterArgs) `
        -WorkingDirectory (Join-Path $projectRoot 'src\TkgPda.Pda') -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput $operatorOut -RedirectStandardError $operatorErr
    $operator.Id | Set-Content -LiteralPath (Join-Path $runtimeDir 'operator.pid')
    $operatorPid = $operator.Id
}

# --- tell the user what is reachable, and what is not -----------------------
$missingRules = @()
foreach ($port in @($ApiPort, $OperatorPort)) {
    $hasRule = Get-NetFirewallRule -Direction Inbound -Enabled True -ErrorAction SilentlyContinue |
        Get-NetFirewallPortFilter -ErrorAction SilentlyContinue |
        Where-Object { $_.Protocol -eq 'TCP' -and $_.LocalPort -eq "$port" }
    if (-not $hasRule) { $missingRules += $port }
}

Write-Host ''
Write-Host 'Reachable from this machine:' -ForegroundColor Cyan
Write-Host "  operator  http://127.0.0.1:$OperatorPort"
foreach ($ip in $lanIps) {
    Write-Host ''
    Write-Host "Reachable from other devices on the network:" -ForegroundColor Cyan
    Write-Host "  operator  http://${ip}:$OperatorPort"
    Write-Host "  api       http://${ip}:$ApiPort"
}
if ($missingRules) {
    Write-Host ''
    Write-Warning "No inbound firewall rule found for TCP $($missingRules -join ', '). Other devices will time out."
    Write-Host 'Run once in an ELEVATED PowerShell:' -ForegroundColor Yellow
    foreach ($port in $missingRules) {
        Write-Host "  New-NetFirewallRule -DisplayName 'TKG PDA $port' -Direction Inbound -Action Allow -Protocol TCP -LocalPort $port -Profile Domain,Private"
    }
}

[pscustomobject]@{
    ApiPid          = $api.Id
    OperatorPid     = $operatorPid
    ApiReady        = $ready
    BoundTo         = '0.0.0.0'
    LanAddresses    = $lanIps
    CorsOrigins     = $corsOrigins
    RdsDispatch     = $false
    MissingFirewall = $missingRules
    ApiLog          = $apiOut
} | ConvertTo-Json -Depth 4
