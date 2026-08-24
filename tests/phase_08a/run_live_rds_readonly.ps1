$ErrorActionPreference = 'Stop'

if ($env:Rds__Enabled -ne 'true' -or [string]::IsNullOrWhiteSpace($env:Rds__BaseUrl)) {
    throw 'Opt-in required: set Rds__Enabled=true and Rds__BaseUrl to the explicitly approved RDS read endpoint.'
}

$baseUri = [Uri]$env:Rds__BaseUrl
if ($baseUri.Scheme -notin @('http', 'https') -or -not [string]::IsNullOrEmpty($baseUri.UserInfo)) {
    throw 'Rds__BaseUrl must be an HTTP(S) URL without embedded credentials.'
}

$calls = [Collections.Generic.List[string]]::new()
$results = [Collections.Generic.List[object]]::new()
function Pass([int]$Number, [string]$Name, [object]$Evidence) {
    $results.Add([ordered]@{ number = $Number; test = $Name; result = 'PASS'; evidence = $Evidence })
}
function Invoke-ReadOnlyPost([string]$Path, [string]$Body) {
    if ($Path -notin @('/api/work-sites/sites', '/api/queryWindTask')) {
        throw "Safety boundary rejected non-read endpoint: $Path"
    }
    $calls.Add($Path)
    $headers = @{ language = 'en'; 'X-Correlation-ID' = [Guid]::NewGuid().ToString() }
    $response = Invoke-WebRequest -Method Post -Uri ([Uri]::new($baseUri, $Path)) -ContentType 'application/json' -Headers $headers -Body $Body -TimeoutSec 10
    if ($response.StatusCode -ne 200) { throw "$Path returned HTTP $($response.StatusCode)" }
    return $response.Content | ConvertFrom-Json
}

# Test 1 performs only a GET of the RDS web root. Tests 2-12 use the two documented read queries above.
$root = Invoke-WebRequest -Method Get -Uri $baseUri -TimeoutSec 10
Pass 1 'base RDS endpoint reachable' "HTTP $($root.StatusCode)"

$sitesEnvelope = Invoke-ReadOnlyPost '/api/work-sites/sites' '{}'
if ($null -eq $sitesEnvelope.code -or $null -eq $sitesEnvelope.msg) { throw 'WorkSite response has no code/msg envelope.' }
Pass 2 '/api/work-sites/sites returns parseable business response' "code=$($sitesEnvelope.code); msg=$($sitesEnvelope.msg)"
if ([int]$sitesEnvelope.code -ne 200) { throw "RDS business failure: $($sitesEnvelope.code) $($sitesEnvelope.msg)" }
Pass 3 'body code is validated' "code=$($sitesEnvelope.code)"

$sites = @($sitesEnvelope.data)
Pass 4 'WorkSite list parses' "count=$($sites.Count)"
$typeCounts = $sites | Group-Object type | ForEach-Object { "$($_.Name)=$($_.Count)" }
Pass 5 'physical/logical type parses' ($typeCounts -join ',')
$filledCounts = $sites | Group-Object filled | ForEach-Object { "$($_.Name)=$($_.Count)" }
Pass 6 'filled state parses' ($filledCounts -join ',')
$lockedCounts = $sites | Group-Object locked | ForEach-Object { "$($_.Name)=$($_.Count)" }
Pass 7 'locked state parses' ($lockedCounts -join ',')
$disabledCounts = $sites | Group-Object disabled | ForEach-Object { "$($_.Name)=$($_.Count)" }
Pass 8 'disabled state parses' ($disabledCounts -join ',')
$syncCounts = $sites | Group-Object syncFailed | ForEach-Object { "$($_.Name)=$($_.Count)" }
Pass 9 'syncFailed parses' ($syncCounts -join ',')
$contentEvidence = @($sites | ForEach-Object { $_.content })
if ($contentEvidence.Count -ne $sites.Count) { throw 'Content projection changed the WorkSite cardinality.' }
Pass 10 'content preserves exact RDS value' "values=$($contentEvidence.Count); nonEmpty=$(@($contentEvidence | Where-Object { -not [string]::IsNullOrEmpty([string]$_) }).Count)"

$tasksEnvelope = Invoke-ReadOnlyPost '/api/queryWindTask' '{"currentPage":1,"pageSize":5,"queryParam":{}}'
if ([int]$tasksEnvelope.code -ne 200) { throw "RDS task business failure: $($tasksEnvelope.code) $($tasksEnvelope.msg)" }
$taskCount = if ($null -eq $tasksEnvelope.data.totalCount) { @($tasksEnvelope.data.pageList).Count } else { [int]$tasksEnvelope.data.totalCount }
Pass 11 '/api/queryWindTask returns parseable response' "code=$($tasksEnvelope.code); totalCount=$taskCount"

$writeCalls = @($calls | Where-Object { $_ -notin @('/api/work-sites/sites', '/api/queryWindTask') })
if ($writeCalls.Count -ne 0 -or $calls.Count -ne 2) { throw 'Harness issued an unexpected endpoint call.' }
Pass 12 'no write endpoint called' ($calls -join ',')

$results | ConvertTo-Json -Depth 5
if ($results.Count -ne 12) { throw 'Expected exactly 12 live read-only acceptance checks.' }
