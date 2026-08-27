param(
    [string]$OutputFile = 'TestResults/dependency-audit.json'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repositoryRoot $OutputFile
$destinationDirectory = Split-Path -Parent $destination
New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null

$json = dotnet list (Join-Path $repositoryRoot 'Nodal.slnx') package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw 'NuGet vulnerability audit did not complete successfully.'
}

$report = $json | ConvertFrom-Json -AsHashtable
$vulnerabilities = [System.Collections.Generic.List[object]]::new()
function Find-Vulnerabilities([object]$value, [string]$projectPath = '') {
    if ($value -is [System.Collections.IDictionary]) {
        if ($value.Contains('path')) { $projectPath = [string]$value['path'] }
        if ($value.Contains('vulnerabilities') -and @($value['vulnerabilities']).Count -gt 0) {
            $vulnerabilities.Add([ordered]@{ project = $projectPath; findings = @($value['vulnerabilities']) })
        }
        foreach ($entry in $value.Values) { Find-Vulnerabilities $entry $projectPath }
    }
    elseif ($value -is [System.Collections.IEnumerable] -and $value -isnot [string]) {
        foreach ($entry in $value) { Find-Vulnerabilities $entry $projectPath }
    }
}
Find-Vulnerabilities $report

[ordered]@{
    schemaVersion = 1
    auditedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    command = 'dotnet list Nodal.slnx package --vulnerable --include-transitive --format json'
    vulnerabilityCount = $vulnerabilities.Count
    findings = $vulnerabilities
    sourceReport = $report
} | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $destination -Encoding utf8

if ($vulnerabilities.Count -gt 0) {
    throw "NuGet vulnerability audit found $($vulnerabilities.Count) affected package entries."
}

Write-Host "NuGet vulnerability audit passed. Evidence: '$destination'."
