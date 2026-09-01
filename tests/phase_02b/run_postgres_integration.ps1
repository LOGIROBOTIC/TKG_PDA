param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$Port = 55436
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$tempRoot = Join-Path $projectRoot '.codex-pg-phase02b-integration'
$dataDir = Join-Path $tempRoot 'data'
$database = 'tkg_pda_phase02b'
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
    Invoke-Checked $psql ($common + @('-f', (Join-Path $projectRoot 'db\seeds\generated\002a_customer_textual_master_data.sql')))
    $phase02bSeed = Join-Path $projectRoot 'db\seeds\generated\002b_customer_physical_layouts.sql'
    Invoke-Checked $psql ($common + @('-f', $phase02bSeed))

    $digestQuery = @"
SELECT md5(jsonb_build_object(
  'layouts', COALESCE((
    SELECT jsonb_agg(to_jsonb(x) ORDER BY x.machine_code, x.code, x.version)
    FROM (
      SELECT l.id, m.code AS machine_code, l.code, l.name, l.version, l.status,
             l.created_at, l.published_at
      FROM master.layouts l JOIN master.machines m ON m.id = l.machine_id
    ) x
  ), '[]'::jsonb),
  'assignments', COALESCE((
    SELECT jsonb_agg(to_jsonb(x) ORDER BY x.machine_code, x.variant_code, x.layout_code)
    FROM (
      SELECT a.variant_id, a.machine_id, a.layout_id, m.code AS machine_code,
             v.code AS variant_code, l.code AS layout_code, a.is_default, a.assigned_at
      FROM master.variant_layout_assignments a
      JOIN master.machines m ON m.id = a.machine_id
      JOIN master.variants v ON v.id = a.variant_id
      JOIN master.layouts l ON l.id = a.layout_id
    ) x
  ), '[]'::jsonb),
  'slots', COALESCE((
    SELECT jsonb_agg(to_jsonb(x) ORDER BY x.machine_code, x.layout_code, x.display_order, x.code)
    FROM (
      SELECT s.id, s.layout_id, m.code AS machine_code, l.code AS layout_code,
             s.code, s.display_name, s.slot_role, s.default_container_type_id,
             s.ui_x, s.ui_y, s.ui_width, s.ui_height, s.display_order,
             s.allows_empty_load, s.allows_full_load, s.is_enabled,
             s.metadata_json, s.created_at, s.updated_at
      FROM master.layout_slots s
      JOIN master.layouts l ON l.id = s.layout_id
      JOIN master.machines m ON m.id = l.machine_id
    ) x
  ), '[]'::jsonb)
)::text);
"@
    $digestBefore = (& $psql @common -Atc $digestQuery).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Initial logical-state digest failed' }

    Invoke-Checked $psql ($common + @('-f', $phase02bSeed))
    $digestAfter = (& $psql @common -Atc $digestQuery).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Second logical-state digest failed' }
    if ($digestBefore -ne $digestAfter) {
        throw "Second Phase 02B import changed logical DB state: $digestBefore -> $digestAfter"
    }

    $verificationQuery = @"
SELECT json_build_object(
  'layouts', (SELECT count(*) FROM master.layouts),
  'assignments', (SELECT count(*) FROM master.variant_layout_assignments),
  'slots', (SELECT count(*) FROM master.layout_slots),
  'machine_slots', (SELECT count(*) FROM master.layout_slots WHERE slot_role = 'MACHINE'),
  'cross_machine_assignments', (
    SELECT count(*)
    FROM master.variant_layout_assignments a
    JOIN master.variants v ON v.id = a.variant_id
    JOIN master.layouts l ON l.id = a.layout_id
    WHERE v.machine_id <> a.machine_id OR l.machine_id <> a.machine_id
  ),
  'orphan_slots', (
    SELECT count(*) FROM master.layout_slots s
    LEFT JOIN master.layouts l ON l.id = s.layout_id
    WHERE l.id IS NULL
  ),
  'duplicate_layouts', (
    SELECT count(*) FROM (
      SELECT machine_id, code, version FROM master.layouts
      GROUP BY machine_id, code, version HAVING count(*) > 1
    ) duplicate
  ),
  'duplicate_slots', (
    SELECT count(*) FROM (
      SELECT layout_id, code FROM master.layout_slots
      GROUP BY layout_id, code HAVING count(*) > 1
    ) duplicate
  ),
  'out_of_bounds_slots', (
    SELECT count(*) FROM master.layout_slots
    WHERE ui_x < 0 OR ui_y < 0 OR ui_width < 0 OR ui_height < 0
       OR ui_x + ui_width > 1.0001 OR ui_y + ui_height > 1.0001
  ),
  'missing_provenance', (
    SELECT count(*) FROM master.layout_slots
    WHERE NOT (metadata_json ? 'source')
       OR coalesce(metadata_json #>> '{source,file}', '') = ''
       OR coalesce(metadata_json #>> '{source,sheet}', '') = ''
       OR coalesce(metadata_json #>> '{source,range}', '') = ''
  ),
  'unknown_footprints', (
    SELECT count(*) FROM master.layout_slots
    WHERE metadata_json ? 'physical_footprint_mm'
      AND metadata_json->'physical_footprint_mm' = 'null'::jsonb
  ),
  'reconciled_sealer_containers', (
    SELECT count(*)
    FROM master.layout_slots s
    JOIN master.layouts l ON l.id = s.layout_id
    JOIN master.machines m ON m.id = l.machine_id
    JOIN master.container_types c ON c.id = s.default_container_type_id
    WHERE m.code = 'SEALER'
      AND s.metadata_json->>'container_evidence' = 'phase_02a_exact_part_cross_context'
      AND c.code IN ('4311', 'KCK')
  ),
  'forbidden_coordinate_keys', (
    SELECT count(*) FROM master.layout_slots
    WHERE metadata_json::text ~ '"(robot_x|robot_y|map_x|map_y|waypoint|route_id|navigation_profile_id|rds_worksite_id|site_id|stitch_config)"[[:space:]]*:'
  ),
  'shared_layouts_with_two_variants', (
    SELECT count(*) FROM (
      SELECT m.code, a.layout_id
      FROM master.variant_layout_assignments a
      JOIN master.machines m ON m.id = a.machine_id
      JOIN master.variants v ON v.id = a.variant_id
      WHERE m.code IN ('SH-01-P1--SH-01-P2', 'SH-02-P1 -SH-02-P2', 'SEALER')
      GROUP BY m.code, a.layout_id
      HAVING count(DISTINCT v.code) = 2
    ) shared
  ),
  'sealer_v3_assignments', (
    SELECT count(*)
    FROM master.variant_layout_assignments a
    JOIN master.machines m ON m.id = a.machine_id
    JOIN master.variants v ON v.id = a.variant_id
    WHERE m.code = 'SEALER' AND v.code = '3'
  ),
  'rds_worksites', (SELECT count(*) FROM integration.rds_worksites),
  'navigation_profiles', (SELECT count(*) FROM master.navigation_profiles)
);
"@
    $actual = (& $psql @common -Atc $verificationQuery) | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'PostgreSQL verification query failed' }
    if ($actual.layouts -ne $expected.layouts) { throw "Layout count mismatch: $($actual.layouts)" }
    if ($actual.assignments -ne $expected.layout_assignments) { throw "Assignment count mismatch: $($actual.assignments)" }
    if ($actual.slots -ne $expected.layout_slots) { throw "Slot count mismatch: $($actual.slots)" }
    if ($actual.machine_slots -ne $expected.machine_region_slots) { throw "Machine-slot count mismatch: $($actual.machine_slots)" }
    if ($actual.cross_machine_assignments -ne 0) { throw 'Cross-machine layout assignment detected' }
    if ($actual.orphan_slots -ne 0) { throw 'Orphan layout slot detected' }
    if ($actual.duplicate_layouts -ne 0 -or $actual.duplicate_slots -ne 0) { throw 'Duplicate layout/slot detected' }
    if ($actual.out_of_bounds_slots -ne 0) { throw 'Out-of-bounds UI geometry detected' }
    if ($actual.missing_provenance -ne 0) { throw 'Slot provenance is incomplete' }
    if ($actual.unknown_footprints -ne $expected.unknown_physical_footprints) { throw 'Unknown footprints were not retained as NULL' }
    if ($actual.reconciled_sealer_containers -ne 6) { throw 'Exact-part SEALER container reconciliation was not loaded' }
    if ($actual.forbidden_coordinate_keys -ne 0) { throw 'Forbidden robot/map/RDS coordinate key detected' }
    if ($actual.shared_layouts_with_two_variants -ne 3) { throw 'Shared-layout assignments are incomplete' }
    if ($actual.sealer_v3_assignments -ne 0) { throw 'SEALER V3 must remain unassigned' }
    if ($actual.rds_worksites -ne 0 -or $actual.navigation_profiles -ne 0) { throw 'RDS/navigation data was seeded unexpectedly' }

    [ordered]@{
        postgresql_version = (& $psql @common -Atc 'SHOW server_version;').Trim()
        phase02b_seed_runs = 2
        layouts = [int]$actual.layouts
        assignments = [int]$actual.assignments
        slots = [int]$actual.slots
        machine_region_slots = [int]$actual.machine_slots
        cross_machine_assignments = [int]$actual.cross_machine_assignments
        orphan_slots = [int]$actual.orphan_slots
        duplicate_layouts = [int]$actual.duplicate_layouts
        duplicate_slots = [int]$actual.duplicate_slots
        out_of_bounds_slots = [int]$actual.out_of_bounds_slots
        missing_provenance = [int]$actual.missing_provenance
        unknown_footprints = [int]$actual.unknown_footprints
        reconciled_sealer_containers = [int]$actual.reconciled_sealer_containers
        forbidden_coordinate_keys = [int]$actual.forbidden_coordinate_keys
        shared_layouts_with_two_variants = [int]$actual.shared_layouts_with_two_variants
        logical_state_digest = $digestAfter
        second_import_changed_state = $false
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
