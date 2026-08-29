param(
    [string]$PgBin = 'C:\Program Files\PostgreSQL\18\bin',
    [int]$PostgresPort = 55446,
    [int]$ApiPort = 5000,
    [string]$RdsBaseUrl = 'http://127.0.0.1:8080'
)

$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$phase08cHarness = Join-Path $projectRoot 'tests\phase_08c\run_live_rds_simulator_e2e.ps1'

function Invoke-RdsPost {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][object]$Body
    )

    return Invoke-RestMethod -Method Post -Uri "$RdsBaseUrl$Path" -Headers @{ language = 'en' } `
        -ContentType 'application/json' -Body ($Body | ConvertTo-Json -Depth 12 -Compress) -TimeoutSec 15
}

function Get-RdsData {
    param([Parameter(Mandatory = $true)][object]$Envelope)
    $data = $Envelope.data
    if ($data -is [string]) { return $data | ConvertFrom-Json }
    return $data
}

if ($env:Rds__Enabled -ne 'true' -or $env:Rds__DispatchEnabled -ne 'true') {
    throw 'Explicit live opt-in required: set Rds__Enabled=true and Rds__DispatchEnabled=true.'
}
if ($env:TKG_PDA_PHASE10A_SIMULATOR_SCENE_CONFIRMED -ne 'true') {
    throw 'Confirm in RDS UI that only sim_01/sim_02 are connected and set TKG_PDA_PHASE10A_SIMULATOR_SCENE_CONFIRMED=true.'
}
if ([string]::IsNullOrWhiteSpace($env:Rds__IntegrationKey) -or $env:Rds__IntegrationKey.Length -lt 32 `
    -or [string]::IsNullOrWhiteSpace($env:Rds__CallbackKey) -or $env:Rds__CallbackKey.Length -lt 32) {
    throw 'Live RDS integration and callback keys must be supplied through process environment.'
}

$rdsUri = [Uri]$RdsBaseUrl
if ($rdsUri.Scheme -ne 'http' -or -not $rdsUri.IsLoopback -or -not [string]::IsNullOrEmpty($rdsUri.UserInfo)) {
    throw 'Phase 10A accepts only the explicitly approved loopback HTTP RDS endpoint.'
}

$validationRecordId = [Guid]::NewGuid().ToString()
$countBeforeEnvelope = Invoke-RdsPost -Path '/api/queryWindTask' -Body @{
    currentPage = 1
    pageSize = 100
    queryParam = @{ taskRecordId = $validationRecordId }
}
$countBefore = Get-RdsData $countBeforeEnvelope
if ([int]$countBefore.totalCount -ne 0) {
    throw 'Fresh validation-only taskRecordId unexpectedly already exists in RDS.'
}

$validationEnvelope = Invoke-RdsPost -Path '/script-api/tkg/validate-transport' -Body @{
    integrationKey = $env:Rds__IntegrationKey
    taskRecordId = $validationRecordId
    taskLabel = 'TKG_SIM_DYNAMIC_MOVE'
    priority = 1
    inputParams = [ordered]@{ from = 'AP20'; to = 'AP15' }
}
$validation = Get-RdsData $validationEnvelope
if ([int]$validationEnvelope.code -ne 200 -or $validationEnvelope.message -ne 'VALIDATED' `
    -or -not $validation.taskLabelExists -or $validation.taskRecordExists) {
    throw 'RDS dynamic task validation did not satisfy the no-create acceptance contract.'
}

$countAfterEnvelope = Invoke-RdsPost -Path '/api/queryWindTask' -Body @{
    currentPage = 1
    pageSize = 100
    queryParam = @{ taskRecordId = $validationRecordId }
}
$countAfter = Get-RdsData $countAfterEnvelope
if ([int]$countAfter.totalCount -ne 0) {
    throw 'Validation-only call created an RDS Wind Task instance.'
}

[ordered]@{
    result = 'VALIDATED'
    validationOnlyTaskRecordId = $validationRecordId
    taskLabel = 'TKG_SIM_DYNAMIC_MOVE'
    taskLabelExists = [bool]$validation.taskLabelExists
    taskRecordExists = [bool]$validation.taskRecordExists
    instanceCountBefore = [int]$countBefore.totalCount
    instanceCountAfter = [int]$countAfter.totalCount
} | ConvertTo-Json -Compress | Write-Output

& $phase08cHarness `
    -PgBin $PgBin `
    -PostgresPort $PostgresPort `
    -ApiPort $ApiPort `
    -RdsBaseUrl $RdsBaseUrl `
    -CommissioningPhase '10a' `
    -TaskLabel 'TKG_SIM_DYNAMIC_MOVE' `
    -FromParameter 'from' `
    -ToParameter 'to' `
    -FromValue 'AP20' `
    -ToValue 'AP15' `
    -SceneConfirmationVariable 'TKG_PDA_PHASE10A_SIMULATOR_SCENE_CONFIRMED' `
    -TerminalTimeoutSeconds 600
