<#
.SYNOPSIS
    Starts the persistent local development stack for TKG PDA.
.DESCRIPTION
    Launches dedicated PostgreSQL instance, ASP.NET Core API, React Admin Web,
    and Flutter Operator Web in local development mode.
    Data is persisted outside the Git repository in $env:LOCALAPPDATA\TKG_PDA_DEV.
    Compatible with Windows PowerShell 5.1.
#>

[CmdletBinding()]
param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$PostgresPort = 55450,
    [string]$Database = 'tkg_pda_dev',
    [int]$ApiPort = 5000,
    [int]$AdminPort = 4173,
    [int]$OperatorPort = 7357,
    [ValidateSet('ReadOnly', 'Off')]
    [string]$RdsMode = 'ReadOnly',
    [string]$RdsBaseUrl = 'http://127.0.0.1:8080',
    [string]$Password,
    [System.Security.SecureString]$SecurePassword,
    [switch]$NoOperator,
    [switch]$NoAdmin
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# Dev directories outside Git repository
$localAppData = [Environment]::GetEnvironmentVariable('LOCALAPPDATA')
if ([string]::IsNullOrWhiteSpace($localAppData)) {
    $localAppData = Join-Path $env:USERPROFILE 'AppData\Local'
}
$devRoot   = Join-Path $localAppData 'TKG_PDA_DEV'
$pgDataDir = Join-Path $devRoot 'postgres'
$logsDir   = Join-Path $devRoot 'logs'
$stateDir  = Join-Path $devRoot 'state'
$stateFile = Join-Path $stateDir 'stack_state.json'

foreach ($dir in @($devRoot, $logsDir, $stateDir)) {
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir -Force | Out-Null
    }
}

# Helper: PBKDF2 hasher in PowerShell 5.1
function New-Pbkdf2Hash51 {
    param([Parameter(Mandatory = $true)][string]$PlainPassword)
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $salt = New-Object byte[] 16
    $rng.GetBytes($salt)
    $rng.Dispose()

    $derive = New-Object System.Security.Cryptography.Rfc2898DeriveBytes(
        $PlainPassword,
        $salt,
        210000,
        [System.Security.Cryptography.HashAlgorithmName]::SHA512
    )
    $hash = $derive.GetBytes(32)
    $derive.Dispose()

    return '$tkg-pbkdf2-sha512$v=1$i=210000$' + [Convert]::ToBase64String($salt) + '$' + [Convert]::ToBase64String($hash)
}

# 1. Initialize PostgreSQL cluster if absent
$pgCtl = Join-Path $PgBin 'pg_ctl.exe'
$initDb = Join-Path $PgBin 'initdb.exe'
$psql = Join-Path $PgBin 'psql.exe'
$createdb = Join-Path $PgBin 'createdb.exe'

if (-not (Test-Path -LiteralPath (Join-Path $pgDataDir 'PG_VERSION'))) {
    Write-Host 'Initializing persistent PostgreSQL development cluster...' -ForegroundColor Cyan
    & $initDb -D $pgDataDir -U postgres -A trust --no-locale -E UTF8
    if ($LASTEXITCODE -ne 0) { throw "initdb failed with exit code $LASTEXITCODE" }
}

# Start PostgreSQL if not already listening
$pgListening = Get-NetTCPConnection -State Listen -LocalPort $PostgresPort -ErrorAction SilentlyContinue
if (-not $pgListening) {
    Write-Host "Starting PostgreSQL on 127.0.0.1:$PostgresPort..." -ForegroundColor Cyan
    $pgLog = Join-Path $logsDir 'postgres.log'
    & $pgCtl -D $pgDataDir -l $pgLog -o "-p $PostgresPort -h 127.0.0.1" -w start
    if ($LASTEXITCODE -ne 0) { throw "pg_ctl start failed with exit code $LASTEXITCODE" }
} else {
    Write-Host "PostgreSQL is already running on port $PostgresPort." -ForegroundColor Green
}

# Helper: safe psql scalar query
function Invoke-DevPsqlScalar {
    param(
        [Parameter(Mandatory = $true)][string]$DatabaseName,
        [Parameter(Mandatory = $true)][string]$Sql
    )
    $output = (& $psql @commonPsql -d $DatabaseName -tAc $Sql)
    if ($null -eq $output) { return '' }
    return ($output -join "`n").Trim()
}

# Create database if absent
$commonPsql = @('-X', '-h', '127.0.0.1', '-p', "$PostgresPort", '-U', 'postgres')
$dbExists = Invoke-DevPsqlScalar 'postgres' "SELECT 1 FROM pg_database WHERE datname = '$Database'"
if ($dbExists -ne '1') {
    Write-Host "Creating development database '$Database'..." -ForegroundColor Cyan
    & $createdb -h '127.0.0.1' -p "$PostgresPort" -U 'postgres' $Database
    if ($LASTEXITCODE -ne 0) { throw "createdb failed with exit code $LASTEXITCODE" }
}

# Apply migrations and seeds idempotently
$schemaExists = Invoke-DevPsqlScalar $Database "SELECT 1 FROM information_schema.tables WHERE table_schema = 'auth' AND table_name = 'roles'"
if ($schemaExists -ne '1') {
    Write-Host 'Applying initial schema (001_initial_schema.sql)...' -ForegroundColor Cyan
    & $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $projectRoot 'db\migrations\001_initial_schema.sql') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Applying 001_initial_schema.sql failed with exit code $LASTEXITCODE" }
}

# Migration 002 is idempotent (CREATE UNIQUE INDEX IF NOT EXISTS); apply it on
# every startup so an existing pre-WP01A database is upgraded in place, while a
# freshly created database (001 above) also receives it. It is a no-op once the
# normalized employee_no index is present.
Write-Host 'Applying migration 002 (002_operator_employee_identity.sql)...' -ForegroundColor Cyan
& $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $projectRoot 'db\migrations\002_operator_employee_identity.sql') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Applying 002_operator_employee_identity.sql failed with exit code $LASTEXITCODE" }
Write-Host 'Applying migration 003 (003_transport_batch.sql)...' -ForegroundColor Cyan
& $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $projectRoot 'db\migrations\003_transport_batch.sql') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Applying 003_transport_batch.sql failed with exit code $LASTEXITCODE" }
Write-Host 'Applying migration 004 (004_transport_request_readiness.sql)...' -ForegroundColor Cyan
& $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $projectRoot 'db\migrations\004_transport_request_readiness.sql') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Applying 004_transport_request_readiness.sql failed with exit code $LASTEXITCODE" }
Write-Host 'Applying migration 005 (005_layout_slot_part_mapping.sql)...' -ForegroundColor Cyan
& $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f (Join-Path $projectRoot 'db\migrations\005_layout_slot_part_mapping.sql') | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Applying 005_layout_slot_part_mapping.sql failed with exit code $LASTEXITCODE" }

Write-Host 'Applying customer master seeds (002a & 002b)...' -ForegroundColor Cyan
$seedFiles = @(
    (Join-Path $projectRoot 'db\seeds\generated\002a_customer_textual_master_data.sql'),
    (Join-Path $projectRoot 'db\seeds\generated\002b_customer_physical_layouts.sql'),
    (Join-Path $projectRoot 'db\seeds\generated\002c_customer_layout_slot_part_assignments.sql')
)
foreach ($sf in $seedFiles) {
    & $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 -f $sf | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Applying seed failed: $sf" }
}

# Resolve development password
$plainPassword = $null
if (-not [string]::IsNullOrWhiteSpace($Password)) {
    $plainPassword = $Password
} elseif ($null -ne $SecurePassword) {
    $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($SecurePassword)
    $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
} else {
    # Check if dev users already exist
    $userCount = Invoke-DevPsqlScalar $Database "SELECT count(*) FROM auth.users WHERE username IN ('dev-admin','dev-supervisor','dev-line','dev-warehouse')"
    if ([string]::IsNullOrWhiteSpace($userCount) -or [int]$userCount -lt 4) {
        $sec = Read-Host -Prompt 'Enter local development password for dev accounts' -AsSecureString
        $bstr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($sec)
        $plainPassword = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# Seed development users, devices, machine runtime, and slot states
if (-not [string]::IsNullOrWhiteSpace($plainPassword)) {
    $passwordHash = New-Pbkdf2Hash51 $plainPassword
    $passwordHashSql = "'" + $passwordHash.Replace("'", "''") + "'"
    $plainPassword = $null
    $passwordHash = $null

    $devBootstrapSql = @"
INSERT INTO auth.users (username, password_hash, full_name, employee_no, is_active)
VALUES
  ('dev-admin', __PASSWORD_HASH__, 'Development Administrator', 'DEV-ADM', true),
  ('dev-supervisor', __PASSWORD_HASH__, 'Development Supervisor', 'DEV-SUP', true),
  ('dev-line', __PASSWORD_HASH__, 'Development Line Operator', 'DEV-LINE', true),
  ('dev-warehouse', __PASSWORD_HASH__, 'Development Warehouse Operator', 'DEV-WH', true)
ON CONFLICT (username) DO UPDATE
SET password_hash = EXCLUDED.password_hash,
    full_name = EXCLUDED.full_name,
    employee_no = EXCLUDED.employee_no,
    is_active = true;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u CROSS JOIN auth.roles r
WHERE u.username = 'dev-admin' AND r.code = 'ADMIN'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u CROSS JOIN auth.roles r
WHERE u.username = 'dev-supervisor' AND r.code = 'SUPERVISOR'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u CROSS JOIN auth.roles r
WHERE u.username = 'dev-line' AND r.code = 'LINE_OPERATOR'
ON CONFLICT DO NOTHING;

INSERT INTO auth.user_roles (user_id, role_id)
SELECT u.id, r.id FROM auth.users u CROSS JOIN auth.roles r
WHERE u.username = 'dev-warehouse' AND r.code = 'WAREHOUSE_OPERATOR'
ON CONFLICT DO NOTHING;
"@
    $devBootstrapSql = $devBootstrapSql.Replace('__PASSWORD_HASH__', $passwordHashSql)
    $devBootstrapSql | & $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Dev user provisioning failed with exit code $LASTEXITCODE" }
}

# Seed devices, permissions, navigation profile, runtime state, and test slot states
$devEnvironmentSql = @'
-- Local development client devices
INSERT INTO audit.client_devices (device_code, name, device_type, is_active)
VALUES
  ('DEV-SUPERVISOR', 'Development Supervisor Terminal', 'PDA', true),
  ('DEV-LINE', 'Development Line Terminal', 'PDA', true),
  ('DEV-WAREHOUSE', 'Development Warehouse Terminal', 'PDA', true)
ON CONFLICT (device_code) DO UPDATE
SET name = EXCLUDED.name, is_active = true;

-- Development Navigation Profile for SH-01-A
INSERT INTO master.navigation_profiles (
    machine_id, code, name, version, status,
    base_map_name, robot_map_name, route_profile_name, stitch_profile_name,
    config_json
)
SELECT
    m.id, 'DEV-RDS-SIM', 'Development RDS Simulator', 1, 'PUBLISHED',
    NULL, NULL, NULL, NULL,
    '{"developmentOnly": true}'::jsonb
FROM master.machines m WHERE m.code = 'SH-01-A'
ON CONFLICT (machine_id, code, version) DO UPDATE
SET status = 'PUBLISHED', config_json = '{"developmentOnly": true}'::jsonb;

-- Assign navigation profile as default for SH-01-A variant 1
INSERT INTO master.variant_navigation_profile_assignments (
    variant_id, machine_id, navigation_profile_id, is_default
)
SELECT
    v.id, m.id, np.id, true
FROM master.machines m
JOIN master.variants v ON v.machine_id = m.id AND v.code = '1'
JOIN master.navigation_profiles np ON np.machine_id = m.id AND np.code = 'DEV-RDS-SIM' AND np.version = 1
WHERE m.code = 'SH-01-A'
ON CONFLICT (variant_id, navigation_profile_id) DO UPDATE SET is_default = true;

-- Permissions on SH-01-A
INSERT INTO auth.user_machine_permissions (user_id, machine_id, can_view, can_operate, can_change_variant, can_manage_slots)
SELECT u.id, m.id, true, true, true, false
FROM auth.users u CROSS JOIN master.machines m
WHERE u.username = 'dev-supervisor' AND m.code = 'SH-01-A'
ON CONFLICT (user_id, machine_id) DO UPDATE
SET can_view = true, can_operate = true, can_change_variant = true, can_manage_slots = false;

INSERT INTO auth.user_machine_permissions (user_id, machine_id, can_view, can_operate, can_change_variant, can_manage_slots)
SELECT u.id, m.id, true, true, false, false
FROM auth.users u CROSS JOIN master.machines m
WHERE u.username = 'dev-line' AND m.code = 'SH-01-A'
ON CONFLICT (user_id, machine_id) DO UPDATE
SET can_view = true, can_operate = true, can_change_variant = false, can_manage_slots = false;

INSERT INTO auth.user_machine_permissions (user_id, machine_id, can_view, can_operate, can_change_variant, can_manage_slots)
SELECT u.id, m.id, true, true, false, false
FROM auth.users u CROSS JOIN master.machines m
WHERE u.username = 'dev-warehouse' AND m.code = 'SH-01-A'
ON CONFLICT (user_id, machine_id) DO UPDATE
SET can_view = true, can_operate = true, can_change_variant = false, can_manage_slots = false;

-- Machine runtime state for SH-01-A
INSERT INTO ops.machine_runtime_state (
    machine_id, active_variant_id, active_layout_id, active_navigation_id
)
SELECT
    m.id, v.id, a.layout_id, np.id
FROM master.machines m
JOIN master.variants v ON v.machine_id = m.id AND v.code = '1'
JOIN master.variant_layout_assignments a ON a.variant_id = v.id AND a.machine_id = m.id AND a.is_default
JOIN master.layouts l ON l.id = a.layout_id AND l.status = 'PUBLISHED'
JOIN master.navigation_profiles np ON np.machine_id = m.id AND np.code = 'DEV-RDS-SIM' AND np.version = 1
WHERE m.code = 'SH-01-A'
ON CONFLICT (machine_id) DO UPDATE
SET active_variant_id = EXCLUDED.active_variant_id,
    active_layout_id = EXCLUDED.active_layout_id,
    active_navigation_id = EXCLUDED.active_navigation_id,
    row_version = ops.machine_runtime_state.row_version + 1,
    updated_at = now();

-- Initial 3 test slot states for SH-01-A default layout
WITH candidate_slots AS (
    SELECT ls.id, ROW_NUMBER() OVER (ORDER BY ls.display_order, ls.code) as rn
    FROM master.machines m
    JOIN master.variants v ON v.machine_id = m.id AND v.code = '1'
    JOIN master.variant_layout_assignments a ON a.variant_id = v.id AND a.machine_id = m.id AND a.is_default
    JOIN master.layout_slots ls ON ls.layout_id = a.layout_id
    WHERE m.code = 'SH-01-A' AND ls.slot_role <> 'MACHINE' AND ls.is_enabled
)
INSERT INTO ops.slot_runtime_state (layout_slot_id, occupancy_state, load_state, current_part_id, current_container_type_id)
SELECT
    cs.id,
    CASE cs.rn
        WHEN 1 THEN 'VACANT'
        WHEN 2 THEN 'OCCUPIED'
        WHEN 3 THEN 'OCCUPIED'
    END,
    CASE cs.rn
        WHEN 1 THEN 'UNKNOWN'
        WHEN 2 THEN 'EMPTY'
        WHEN 3 THEN 'FULL'
    END,
    NULL,
    NULL
FROM candidate_slots cs
WHERE cs.rn IN (1, 2, 3)
ON CONFLICT (layout_slot_id) DO UPDATE
SET occupancy_state = EXCLUDED.occupancy_state,
    load_state = EXCLUDED.load_state,
    current_part_id = NULL,
    current_container_type_id = NULL,
    row_version = ops.slot_runtime_state.row_version + 1,
    updated_at = now();
'@
$devEnvironmentSql | & $psql @commonPsql -d $Database -v ON_ERROR_STOP=1 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Dev environment seeding failed with exit code $LASTEXITCODE" }

# 2. Build .NET Solution in Debug mode
Write-Host 'Building .NET solution (Debug)...' -ForegroundColor Cyan
& dotnet build (Join-Path $projectRoot 'TKG.Pda.slnx') --configuration Debug --no-restore
if ($LASTEXITCODE -ne 0) {
    & dotnet build (Join-Path $projectRoot 'TKG.Pda.slnx') --configuration Debug
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

# 3. Generate JWT signing key
$jwtRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtBytes = New-Object byte[] 48
$jwtRng.GetBytes($jwtBytes)
$jwtRng.Dispose()
$jwtSigningKey = [Convert]::ToBase64String($jwtBytes)

# 4. Check if API is already running; stop previous instance if any
if (Test-Path -LiteralPath $stateFile) {
    try {
        $oldState = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ($oldState.apiPid) {
            $p = Get-Process -Id $oldState.apiPid -ErrorAction SilentlyContinue
            if ($p) { Stop-Process -Id $oldState.apiPid -Force -ErrorAction SilentlyContinue }
        }
        if ($oldState.adminPid) {
            $p = Get-Process -Id $oldState.adminPid -ErrorAction SilentlyContinue
            if ($p) { Stop-Process -Id $oldState.adminPid -Force -ErrorAction SilentlyContinue }
        }
        if ($oldState.operatorPid) {
            $p = Get-Process -Id $oldState.operatorPid -ErrorAction SilentlyContinue
            if ($p) { Stop-Process -Id $oldState.operatorPid -Force -ErrorAction SilentlyContinue }
        }
    } catch {
        # ignore state read error
    }
}

# 5. Start API
Write-Host "Starting ASP.NET Core API on http://127.0.0.1:$ApiPort..." -ForegroundColor Cyan
$apiStdOut = Join-Path $logsDir 'api.stdout.log'
$apiStdErr = Join-Path $logsDir 'api.stderr.log'

$env:ConnectionStrings__TkgPda = "Host=127.0.0.1;Port=$PostgresPort;Database=$Database;Username=postgres;Pooling=true;Maximum Pool Size=50"
$env:ASPNETCORE_URLS = "http://127.0.0.1:$ApiPort"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:Cors__AllowedOrigins__0 = "http://127.0.0.1:$OperatorPort"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:Authentication__Jwt__SigningKey = $jwtSigningKey

if ($RdsMode -eq 'ReadOnly') {
    $env:Rds__Enabled = 'true'
    $env:Rds__DispatchEnabled = 'false'
    $env:Rds__BaseUrl = $RdsBaseUrl
} else {
    $env:Rds__Enabled = 'false'
    $env:Rds__DispatchEnabled = 'false'
    $env:Rds__BaseUrl = ''
}

$apiProcess = Start-Process -FilePath 'dotnet' -ArgumentList @(
    'run', '--project', (Join-Path $projectRoot 'src\TkgPda.Api\TkgPda.Api.csproj'),
    '--configuration', 'Debug', '--no-build', '--no-launch-profile'
) -PassThru -WindowStyle Hidden -RedirectStandardOutput $apiStdOut -RedirectStandardError $apiStdErr

# Wait for API health/ready
$apiReady = $false
for ($i = 0; $i -lt 40; $i++) {
    if ($apiProcess.HasExited) {
        $errText = if (Test-Path -LiteralPath $apiStdErr) { Get-Content -LiteralPath $apiStdErr -Tail 20 | Out-String } else { '' }
        throw "API process exited unexpectedly with code $($apiProcess.ExitCode). $errText"
    }
    try {
        $readyResp = Invoke-RestMethod -Method Get -Uri "http://127.0.0.1:$ApiPort/health/ready" -TimeoutSec 2
        if ($readyResp.status -eq 'ready') {
            $apiReady = $true
            break
        }
    } catch {
        Start-Sleep -Milliseconds 500
    }
}
if (-not $apiReady) { throw "API did not become ready on port $ApiPort." }
Write-Host "API is ready on http://127.0.0.1:$ApiPort." -ForegroundColor Green

# 6. Start Admin Web
$adminProcess = $null
if (-not $NoAdmin) {
    $adminDir = Join-Path $projectRoot 'src\TkgPda.AdminWeb'
    if (-not (Test-Path -LiteralPath (Join-Path $adminDir 'node_modules'))) {
        Write-Host 'Installing Admin Web node_modules...' -ForegroundColor Cyan
        & cmd.exe /c "cd /d `"$adminDir`" && npm ci"
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed in AdminWeb with exit code $LASTEXITCODE" }
    }

    Write-Host "Starting Admin Web on http://127.0.0.1:$AdminPort..." -ForegroundColor Cyan
    $adminStdOut = Join-Path $logsDir 'admin_web.stdout.log'
    $adminStdErr = Join-Path $logsDir 'admin_web.stderr.log'
    $env:TKG_API_TARGET = "http://127.0.0.1:$ApiPort"

    $adminProcess = Start-Process -FilePath 'cmd.exe' -ArgumentList @(
        '/c', 'npm run dev'
    ) -WorkingDirectory $adminDir -PassThru -WindowStyle Hidden -RedirectStandardOutput $adminStdOut -RedirectStandardError $adminStdErr
}

# 7. Start Flutter Operator Web
$operatorProcess = $null
if (-not $NoOperator) {
    $pdaDir = Join-Path $projectRoot 'src\TkgPda.Pda'
    Write-Host "Starting Flutter Operator Web on http://127.0.0.1:$OperatorPort..." -ForegroundColor Cyan
    $operatorStdOut = Join-Path $logsDir 'operator_web.stdout.log'
    $operatorStdErr = Join-Path $logsDir 'operator_web.stderr.log'

    $operatorProcess = Start-Process -FilePath 'cmd.exe' -ArgumentList @(
        '/c', "flutter run -d chrome --web-hostname=127.0.0.1 --web-port=$OperatorPort --dart-define=TKG_API_BASE_URL=http://127.0.0.1:$ApiPort"
    ) -WorkingDirectory $pdaDir -PassThru -WindowStyle Hidden -RedirectStandardOutput $operatorStdOut -RedirectStandardError $operatorStdErr
}

# Check RDS reachability
$rdsStatusDisplay = 'OFF'
if ($RdsMode -eq 'ReadOnly') {
    try {
        $rdsCheck = Invoke-RestMethod -Method Get -Uri $RdsBaseUrl -TimeoutSec 3
        $rdsStatusDisplay = "READ-ONLY / $RdsBaseUrl"
    } catch {
        $rdsStatusDisplay = "READ-ONLY (UNAVAILABLE) / $RdsBaseUrl"
    }
}

# Record state
$state = [ordered]@{
    startedAt       = (Get-Date).ToString('o')
    postgresPort    = $PostgresPort
    database        = $Database
    pgDataDir       = $pgDataDir
    apiPort         = $ApiPort
    apiPid          = $apiProcess.Id
    adminPort       = $AdminPort
    adminPid        = if ($adminProcess) { $adminProcess.Id } else { $null }
    operatorPort    = $OperatorPort
    operatorPid     = if ($operatorProcess) { $operatorProcess.Id } else { $null }
    rdsMode         = $RdsMode
    rdsBaseUrl      = $RdsBaseUrl
}
$state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $stateFile -Encoding UTF8

# Report per Section 19 specification
Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "TKG LOCAL DEV READY" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "PostgreSQL:"
Write-Host "127.0.0.1:$PostgresPort / $Database"
Write-Host ""
Write-Host "API:"
Write-Host "http://127.0.0.1:$ApiPort"
Write-Host ""
if (-not $NoAdmin) {
    Write-Host "Admin:"
    Write-Host "http://127.0.0.1:$AdminPort"
    Write-Host ""
}
if (-not $NoOperator) {
    Write-Host "Operator:"
    Write-Host "http://127.0.0.1:$OperatorPort"
    Write-Host ""
}
Write-Host "RDS:"
Write-Host $rdsStatusDisplay
Write-Host ""
Write-Host "Accounts:"
Write-Host "dev-admin"
Write-Host "dev-supervisor"
Write-Host "dev-line"
Write-Host "dev-warehouse"
Write-Host ""
Write-Host "Device Codes:"
Write-Host "DEV-SUPERVISOR"
Write-Host "DEV-LINE"
Write-Host "DEV-WAREHOUSE"
Write-Host ""
Write-Host "RDS DISPATCH DISABLED" -ForegroundColor Yellow
Write-Host "========================================" -ForegroundColor Green
