param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55435
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$tempRoot = Join-Path $projectRoot '.codex-pg-phase02a-integration'
$dataDir = Join-Path $tempRoot 'data'
$database = 'tkg_pda_phase02a'
$expected = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'expected_counts.json') -Raw | ConvertFrom-Json
$started = $false

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Executable,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Executable failed with exit code $LASTEXITCODE"
    }
}

if (Test-Path -LiteralPath $tempRoot) {
    throw "Temporary integration path already exists: $tempRoot"
}
if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
    throw "Integration-test port is already in use: $Port"
}

try {
    Invoke-Checked (Join-Path $PgBin 'initdb.exe') @(
        '-D', $dataDir, '-U', 'postgres', '-A', 'trust', '--no-locale', '-E', 'UTF8'
    )
    Invoke-Checked (Join-Path $PgBin 'pg_ctl.exe') @(
        '-D', $dataDir,
        '-l', (Join-Path $tempRoot 'postgres.log'),
        '-o', "-p $Port -h 127.0.0.1",
        '-w', 'start'
    )
    $started = $true

    Invoke-Checked (Join-Path $PgBin 'createdb.exe') @(
        '-h', '127.0.0.1', '-p', "$Port", '-U', 'postgres', $database
    )

    $psql = Join-Path $PgBin 'psql.exe'
    $common = @('-X', '-h', '127.0.0.1', '-p', "$Port", '-U', 'postgres', '-d', $database, '-v', 'ON_ERROR_STOP=1')
    Invoke-Checked $psql ($common + @('-f', (Join-Path $projectRoot 'db\migrations\001_initial_schema.sql')))
    $seed = Join-Path $projectRoot 'db\seeds\generated\002a_customer_textual_master_data.sql'
    Invoke-Checked $psql ($common + @('-f', $seed))
    Invoke-Checked $psql ($common + @('-f', $seed))

    $countsText = & $psql @common -Atc @"
SELECT json_build_object(
  'parts', (SELECT count(*) FROM master.parts),
  'containers', (SELECT count(*) FROM master.container_types),
  'groups', (SELECT count(*) FROM master.product_groups),
  'members', (SELECT count(*) FROM master.product_group_parts),
  'planning', (SELECT count(*) FROM master.part_planning_metrics),
  'reconciled_container_dimensions', (
    SELECT count(*) FROM master.container_types
    WHERE (code, category, length_mm, width_mm, height_mm) IN (
      ('1284', 'CONTAINER', 1200, 800, 400),
      ('BYK', 'CONTAINER', 1200, 750, 600),
      ('ORT', 'CONTAINER', 950, 650, 550),
      ('KCK', 'CONTAINER', 800, 550, 450)
    )
  ),
  'reconciled_category_errors', (
    SELECT count(*) FROM master.container_types
    WHERE (code = '285C' AND category <> 'CONTAINER')
       OR (code = 'ARABA' AND category <> 'CART')
       OR (code = '1285' AND category <> 'OTHER')
  ),
  'orphan_groups', (
    SELECT count(*) FROM master.product_groups pg
    LEFT JOIN master.variants v ON v.id = pg.variant_id
    LEFT JOIN master.parts p ON p.id = pg.main_part_id
    WHERE v.id IS NULL OR p.id IS NULL
  ),
  'orphan_planning', (
    SELECT count(*) FROM master.part_planning_metrics ppm
    LEFT JOIN master.variants v ON v.id = ppm.variant_id
    LEFT JOIN master.product_groups pg ON pg.id = ppm.product_group_id
    LEFT JOIN master.parts p ON p.id = ppm.part_id
    WHERE v.id IS NULL OR pg.id IS NULL OR p.id IS NULL
  ),
  'known_null_metric_rows', (
    SELECT count(*)
    FROM master.part_planning_metrics ppm
    JOIN master.parts p ON p.id = ppm.part_id
    WHERE p.part_code IN (
      '715A2BN400-50','715B2BN400-50','90174-12022','90179-10234',
      '90179-14075','72613-02210','57658-K0020'
    )
      AND ppm.lot_quantity IS NULL
      AND ppm.agv_moves_per_day IS NULL
  )
);
"@
    if ($LASTEXITCODE -ne 0) {
        throw "PostgreSQL verification query failed with exit code $LASTEXITCODE"
    }
    $actual = $countsText | ConvertFrom-Json
    if ($actual.parts -ne $expected.unique_parts) { throw "Part count mismatch: $($actual.parts)" }
    if ($actual.containers -ne $expected.container_types) { throw "Container count mismatch: $($actual.containers)" }
    if ($actual.groups -ne $expected.product_groups) { throw "Product-group count mismatch: $($actual.groups)" }
    if ($actual.members -ne $expected.product_group_parts) { throw "Group-member count mismatch: $($actual.members)" }
    if ($actual.planning -ne $expected.planning_rows) { throw "Planning-row count mismatch: $($actual.planning)" }
    if ($actual.reconciled_container_dimensions -ne 4) { throw 'Customer container dimensions were not loaded as reconciled' }
    if ($actual.reconciled_category_errors -ne 0) { throw 'Customer container category reconciliation failed' }
    if ($actual.orphan_groups -ne 0 -or $actual.orphan_planning -ne 0) { throw 'Referential-integrity verification failed' }
    if ($actual.known_null_metric_rows -lt 9) { throw 'Known incomplete planning rows were not retained with NULL metrics' }

    [ordered]@{
        postgresql_version = (& $psql @common -Atc 'SHOW server_version;').Trim()
        seed_runs = 2
        parts = [int]$actual.parts
        container_types = [int]$actual.containers
        product_groups = [int]$actual.groups
        product_group_parts = [int]$actual.members
        planning_rows = [int]$actual.planning
        reconciled_container_dimensions = [int]$actual.reconciled_container_dimensions
        reconciled_category_errors = [int]$actual.reconciled_category_errors
        known_null_metric_rows = [int]$actual.known_null_metric_rows
        orphan_groups = [int]$actual.orphan_groups
        orphan_planning = [int]$actual.orphan_planning
        result = 'PASS'
    } | ConvertTo-Json
}
finally {
    if ($started) {
        & (Join-Path $PgBin 'pg_ctl.exe') -D $dataDir -m fast -w stop
    }
    if (Test-Path -LiteralPath $tempRoot) {
        $resolved = (Resolve-Path -LiteralPath $tempRoot).Path
        if ($resolved -ne $tempRoot) {
            throw "Unexpected temporary cleanup target: $resolved"
        }
        Add-Type -AssemblyName Microsoft.VisualBasic
        [Microsoft.VisualBasic.FileIO.FileSystem]::DeleteDirectory(
            $resolved,
            [Microsoft.VisualBasic.FileIO.UIOption]::OnlyErrorDialogs,
            [Microsoft.VisualBasic.FileIO.RecycleOption]::SendToRecycleBin
        )
    }
}
