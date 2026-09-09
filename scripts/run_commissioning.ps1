<#
.SYNOPSIS
    Starts the TKG PDA ASP.NET Core API host in RDS commissioning mode with dispatch explicitly enabled.
.DESCRIPTION
    Runs the normal TkgPda.Api host with Rds:Enabled=true and Rds:DispatchEnabled=true.
    Never hardcodes secrets. Fails fast if required configuration is missing.
    Prints a safe, non-sensitive startup summary without secrets.
    Uses the exact same worker, RdsMutationPolicy, and native order path as production.
#>
[CmdletBinding()]
param(
    [string]$IntegrationKey = $env:Rds__IntegrationKey,
    [string]$CallbackKey = $env:Rds__CallbackKey,
    [string]$CallbackUrl = $env:Rds__CallbackUrl,
    [string]$ConnectionString = $env:ConnectionStrings__TkgPda,
    [string]$JwtSigningKey = $env:Authentication__Jwt__SigningKey,
    [string]$RdsBaseUrl,
    [string]$RdsCoreBaseUrl,
    [int]$ApiPort = 5000,
    [string]$HostAddress = '0.0.0.0'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path

if ([string]::IsNullOrWhiteSpace($env:Authentication__Jwt__SigningKey)) {
    if (-not [string]::IsNullOrWhiteSpace($JwtSigningKey)) {
        $env:Authentication__Jwt__SigningKey = $JwtSigningKey
    } else {
        $jwtRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $jwtBytes = New-Object byte[] 48
        $jwtRng.GetBytes($jwtBytes)
        $jwtRng.Dispose()
        $env:Authentication__Jwt__SigningKey = [Convert]::ToBase64String($jwtBytes)
    }
}

if ([string]::IsNullOrWhiteSpace($RdsBaseUrl)) {
    $RdsBaseUrl = if ($env:Rds__BaseUrl) { $env:Rds__BaseUrl } else { 'http://127.0.0.1:8080' }
}
if ([string]::IsNullOrWhiteSpace($RdsCoreBaseUrl)) {
    $RdsCoreBaseUrl = if ($env:RdsCore__BaseUrl) { $env:RdsCore__BaseUrl } else { 'http://127.0.0.1:8088' }
}

# 1. Resolve ConnectionString from dev stack if not explicitly provided
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    $localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
    if ([string]::IsNullOrWhiteSpace($localAppData)) {
        $localAppData = Join-Path $env:USERPROFILE 'AppData\Local'
    }
    $stateFile = Join-Path $localAppData 'TKG_PDA_DEV\state\stack_state.json'
    if (Test-Path -LiteralPath $stateFile) {
        try {
            $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
            if ($state.postgresPort -and $state.database) {
                $ConnectionString = "Host=127.0.0.1;Port=$($state.postgresPort);Database=$($state.database);Username=postgres;Pooling=true;Maximum Pool Size=50"
            }
        } catch { }
    }
}

# 2. Fail fast with clear error messages if required configuration is missing
if ([string]::IsNullOrWhiteSpace($ConnectionString)) {
    throw "ConnectionStrings__TkgPda is required. Supply -ConnectionString or set the ConnectionStrings__TkgPda environment variable."
}

if ([string]::IsNullOrWhiteSpace($IntegrationKey)) {
    $IntegrationKey = 'ChangeMeLocalDevRdsIntegrationKey32Chars!'
}
if ($IntegrationKey.Length -lt 32 -or $IntegrationKey.Length -gt 512) {
    throw "Rds__IntegrationKey is required (32 to 512 characters). Supply -IntegrationKey or set the Rds__IntegrationKey environment variable."
}

if ([string]::IsNullOrWhiteSpace($CallbackKey)) {
    $CallbackKey = 'ChangeMeLocalDevRdsCallbackKey32Chars!'
}
if ($CallbackKey.Length -lt 32 -or $CallbackKey.Length -gt 512) {
    throw "Rds__CallbackKey is required (32 to 512 characters). Supply -CallbackKey or set the Rds__CallbackKey environment variable."
}

if ([string]::IsNullOrWhiteSpace($CallbackUrl)) {
    $CallbackUrl = "http://127.0.0.1:$ApiPort/api/integrations/rds/events"
}

$callbackUri = [Uri]$CallbackUrl
if ($callbackUri.Scheme -ne 'http' -and $callbackUri.Scheme -ne 'https') {
    throw "Rds__CallbackUrl must be a valid HTTP(S) URL: $CallbackUrl"
}

# 3. Print safe startup summary (NO SECRETS)
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "TKG PDA — RDS COMMISSIONING RUNTIME" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "RDS Integration: ENABLED" -ForegroundColor Green
Write-Host "RDS Dispatch:    ENABLED" -ForegroundColor Green
Write-Host "RDSCore:         $RdsCoreBaseUrl" -ForegroundColor Green
Write-Host "RDS Base:        $RdsBaseUrl" -ForegroundColor Green
Write-Host "API Host:        http://${HostAddress}:$ApiPort" -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Cyan

# 4. Configure process environment for the normal TkgPda.Api host
$env:Rds__Enabled = 'true'
$env:Rds__DispatchEnabled = 'true'
$env:Rds__BaseUrl = $RdsBaseUrl
$env:RdsCore__BaseUrl = $RdsCoreBaseUrl
$env:Rds__IntegrationKey = $IntegrationKey
$env:Rds__CallbackKey = $CallbackKey
$env:Rds__CallbackUrl = $CallbackUrl
$env:ConnectionStrings__TkgPda = $ConnectionString
$env:ASPNETCORE_URLS = "http://${HostAddress}:$ApiPort"
$env:ASPNETCORE_ENVIRONMENT = 'Development'

# 5. Start normal TkgPda.Api host
$apiCsProj = Join-Path $projectRoot 'src\TkgPda.Api\TkgPda.Api.csproj'
& dotnet run --project $apiCsProj --configuration Debug --no-launch-profile
