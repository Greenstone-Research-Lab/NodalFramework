param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$CommitSha,

    [string]$OutputDirectory = 'TestResults/release-evidence',

    [string]$PackageDirectory = 'TestResults/package-verification'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$capabilityGraph = Join-Path $repositoryRoot 'website/static/knowledge/nodal-capabilities.jsonld'
if (-not (Test-Path -LiteralPath $capabilityGraph -PathType Leaf)) {
    throw 'The canonical capability knowledge graph was not found.'
}

$destination = Join-Path $repositoryRoot $OutputDirectory
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$packagePath = Join-Path $repositoryRoot $PackageDirectory
$dependencyAudit = Join-Path $destination 'dependency-audit.json'
$reproducibility = Join-Path $repositoryRoot 'TestResults/reproducible-packages/reproducibility.json'
$sbom = Join-Path $destination 'sbom/_manifest/spdx_2.2/manifest.spdx.json'

foreach ($requiredPath in @($packagePath, $dependencyAudit, $reproducibility, $sbom)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required release evidence '$requiredPath' was not found."
    }
}

$packageArtifacts = @(Get-ChildItem -LiteralPath $packagePath -File |
    Where-Object Extension -In '.nupkg', '.snupkg' |
    Sort-Object Name |
    ForEach-Object {
        [ordered]@{
            file = $_.Name
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })
if ($packageArtifacts.Count -ne 18) {
    throw "Expected 18 release package artifacts but found $($packageArtifacts.Count)."
}

$reproducibilityReport = Get-Content -LiteralPath $reproducibility -Raw | ConvertFrom-Json
if ($reproducibilityReport.reproducible -ne $true -or
    $reproducibilityReport.packageVersion -ne $PackageVersion -or
    $reproducibilityReport.artifactCount -ne 18) {
    throw 'The reproducibility report does not describe this complete release artifact set.'
}

$reproducibleArtifacts = @{}
foreach ($artifact in @($reproducibilityReport.artifacts)) {
    $reproducibleArtifacts[[string]$artifact.file] = [string]$artifact.sha256
}
foreach ($artifact in $packageArtifacts) {
    if (-not $reproducibleArtifacts.ContainsKey($artifact.file) -or
        $reproducibleArtifacts[$artifact.file] -ne $artifact.sha256) {
        throw "Release artifact '$($artifact.file)' does not match the reproducibility report."
    }
}

$report = [ordered]@{
    schemaVersion = 2
    packageVersion = $PackageVersion
    commitSha = $CommitSha
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    targetFramework = 'net10.0'
    migrationBundleFormat = 1
    packages = @(
        'Nodal.Core'
        'Nodal.Migrations'
        'Nodal.Neo4j'
        'Nodal.Analytics'
        'Nodal.TigerGraph'
        'Nodal.Tool'
        'Nodal.Import'
        'Nodal.Import.Csv'
        'Nodal.Import.Relational'
    )
    verifiedProviders = @(
        [ordered]@{ name = 'Neo4j'; version = '5.26 Community'; transport = 'Neo4j.Driver 6.3.0' }
        [ordered]@{ name = 'TigerGraph'; version = '4.2.4 Community'; transport = 'REST++ / GSQL' }
    )
    capabilityGraph = [ordered]@{
        file = 'nodal-capabilities.jsonld'
        sha256 = (Get-FileHash -LiteralPath $capabilityGraph -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    packageArtifacts = $packageArtifacts
    evidence = [ordered]@{
        dependencyAudit = [ordered]@{
            file = 'dependency-audit.json'
            sha256 = (Get-FileHash -LiteralPath $dependencyAudit -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        reproducibility = [ordered]@{
            file = 'reproducibility.json'
            sha256 = (Get-FileHash -LiteralPath $reproducibility -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        sbom = [ordered]@{
            file = 'sbom/_manifest/spdx_2.2/manifest.spdx.json'
            sha256 = (Get-FileHash -LiteralPath $sbom -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
}

$reportPath = Join-Path $destination 'nodal-release-evidence.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Copy-Item -LiteralPath $capabilityGraph -Destination (Join-Path $destination 'nodal-capabilities.jsonld') -Force
Copy-Item -LiteralPath $reproducibility -Destination (Join-Path $destination 'reproducibility.json') -Force
Write-Host "Release evidence written to '$reportPath'."
