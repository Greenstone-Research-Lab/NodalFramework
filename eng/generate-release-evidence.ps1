param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [Parameter(Mandatory = $true)]
    [string]$CommitSha,

    [string]$OutputDirectory = 'TestResults/release-evidence'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$capabilityGraph = Join-Path $repositoryRoot 'website/static/knowledge/nodal-capabilities.jsonld'
if (-not (Test-Path -LiteralPath $capabilityGraph -PathType Leaf)) {
    throw 'The canonical capability knowledge graph was not found.'
}

$destination = Join-Path $repositoryRoot $OutputDirectory
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$report = [ordered]@{
    schemaVersion = 1
    packageVersion = $PackageVersion
    commitSha = $CommitSha
    targetFramework = 'net10.0'
    migrationBundleFormat = 1
    packages = @(
        'Nodal.Core'
        'Nodal.Migrations'
        'Nodal.Neo4j'
        'Nodal.Analytics'
        'Nodal.PatternRecognition'
        'Nodal.TigerGraph'
        'Nodal.Tool'
    )
    verifiedProviders = @(
        [ordered]@{ name = 'Neo4j'; version = '5.26 Community'; transport = 'Neo4j.Driver 6.3.0' }
        [ordered]@{ name = 'TigerGraph'; version = '4.2.4 Community'; transport = 'REST++ / GSQL' }
    )
    capabilityGraph = [ordered]@{
        file = 'nodal-capabilities.jsonld'
        sha256 = (Get-FileHash -LiteralPath $capabilityGraph -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

$reportPath = Join-Path $destination 'nodal-release-evidence.json'
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding utf8
Copy-Item -LiteralPath $capabilityGraph -Destination (Join-Path $destination 'nodal-capabilities.jsonld') -Force
Write-Host "Release evidence written to '$reportPath'."
