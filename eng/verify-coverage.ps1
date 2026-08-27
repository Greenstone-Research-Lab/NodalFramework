param(
    [double]$Threshold = 95
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$runId = [guid]::NewGuid().ToString('N')
$targets = @(
    @{ Package = 'Nodal.Core'; Project = 'tests/Nodal.Core.Tests/Nodal.Core.Tests.csproj' },
    @{ Package = 'Nodal.Migrations'; Project = 'tests/Nodal.Migrations.Tests/Nodal.Migrations.Tests.csproj' },
    @{ Package = 'Nodal.Neo4j'; Project = 'tests/Nodal.Neo4j.Tests/Nodal.Neo4j.Tests.csproj' },
    @{ Package = 'Nodal.TigerGraph'; Project = 'tests/Nodal.TigerGraph.Tests/Nodal.TigerGraph.Tests.csproj' },
    @{ Package = 'Nodal.Tool'; Project = 'tests/Nodal.Tool.Tests/Nodal.Tool.Tests.csproj' },
    @{ Package = 'Nodal.Import'; Project = 'tests/Nodal.Import.Tests/Nodal.Import.Tests.csproj' },
    @{ Package = 'Nodal.Import.Csv'; Project = 'tests/Nodal.Import.Tests/Nodal.Import.Tests.csproj' },
    @{ Package = 'Nodal.Import.Relational'; Project = 'tests/Nodal.Import.Tests/Nodal.Import.Tests.csproj' }
)

foreach ($target in $targets) {
    $packageName = $target.Package
    $projectPath = Join-Path $repositoryRoot $target.Project
    $resultsPath = Join-Path $repositoryRoot "TestResults/Coverage/$runId/$packageName"
    $outputPath = Join-Path $repositoryRoot "TestResults/CoverageOutput/$runId/$packageName"

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
        throw "Coverlet did not produce a coverage report for '$packageName'."
    }

    [xml]$coverage = Get-Content -LiteralPath $reportPath
    $package = $coverage.coverage.packages.package |
        Where-Object { $_.name -eq $packageName } |
        Select-Object -First 1
    if (-not $package) {
        throw "Coverage report does not contain package '$packageName'."
    }

    $lineHits = @{}
    foreach ($class in $package.classes.class) {
        $sourceFile = [string]$class.filename
        if ($sourceFile -match '(^|[\\/])obj([\\/]|$)') {
            continue
        }

        foreach ($line in $class.lines.line) {
            $key = "$sourceFile|$($line.number)"
            $hits = [int]$line.hits
            if (-not $lineHits.ContainsKey($key) -or $hits -gt $lineHits[$key]) {
                $lineHits[$key] = $hits
            }
        }
    }

    if ($lineHits.Count -eq 0) {
        throw "Coverage report contains no maintainable source lines for '$packageName'."
    }

    $coveredLines = @($lineHits.Values | Where-Object { $_ -gt 0 }).Count
    $lineRate = $coveredLines / $lineHits.Count * 100
    Write-Host ("{0} line coverage: {1:N2}% ({2}/{3})" -f `
        $packageName, $lineRate, $coveredLines, $lineHits.Count)

    if ($lineRate -lt $Threshold) {
        throw ("{0} line coverage {1:N2}% is below the required {2:N2}%." -f `
            $packageName, $lineRate, $Threshold)
    }
}

Write-Host ("All product packages satisfy the {0:N2}% line coverage gate." -f $Threshold)
