param(
    [double]$Threshold = 95
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'tests/Nodal.Core.Tests/Nodal.Core.Tests.csproj'
$runId = [guid]::NewGuid().ToString('N')
$resultsPath = Join-Path $repositoryRoot "TestResults/CoreCoverage/$runId"
$outputPath = Join-Path $repositoryRoot "TestResults/CoreCoverageOutput/$runId"

dotnet test $projectPath `
    --configuration Debug `
    --no-restore `
    --output $outputPath `
    --collect 'XPlat Code Coverage' `
    --results-directory $resultsPath
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$reportPath = Get-ChildItem $resultsPath -Recurse -Filter 'coverage.cobertura.xml' |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $reportPath) {
    throw 'Coverlet did not produce a Cobertura coverage report.'
}

[xml]$coverage = Get-Content $reportPath
$lineRate = [double]$coverage.coverage.'line-rate' * 100
$branchRate = [double]$coverage.coverage.'branch-rate' * 100

Write-Host ('Core line coverage: {0:N2}%' -f $lineRate)
Write-Host ('Core branch coverage: {0:N2}%' -f $branchRate)

if ($lineRate -lt $Threshold) {
    throw ('Core line coverage {0:N2}% is below the required {1:N2}%.' -f $lineRate, $Threshold)
}
