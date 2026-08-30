param(
    [string]$PackageVersion = '0.1.0-beta.1'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repositoryRoot 'consumer-smoke/WorldFoodDelivery/WorldFoodDelivery.csproj'
$readmePath = Join-Path $repositoryRoot 'consumer-smoke/WorldFoodDelivery/README.md'
$installationPath = Join-Path $repositoryRoot 'website/docs/installation.md'
$modelingPath = Join-Path $repositoryRoot 'website/docs/modeling.md'
$expectedPackages = @(
    'Nodal.Core'
    'Nodal.Analytics'
    'Nodal.Import.Csv'
    'Nodal.Import.Relational'
    'Nodal.Migrations'
    'Nodal.Neo4j'
    'Nodal.TigerGraph'
)

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$references = @($project.Project.ItemGroup.PackageReference | ForEach-Object Include)
foreach ($package in $expectedPackages) {
    if ($package -notin $references) {
        throw "The canonical documentation consumer is missing '$package'."
    }
}
if ($project.Project.ItemGroup.ProjectReference) {
    throw 'The canonical documentation consumer must not contain ProjectReference entries.'
}

$readme = Get-Content -LiteralPath $readmePath -Raw
$installation = Get-Content -LiteralPath $installationPath -Raw
$modeling = Get-Content -LiteralPath $modelingPath -Raw
foreach ($requiredText in @('package-only', 'World Food Delivery', 'model generate', 'model diff')) {
    if (($readme + $installation + $modeling) -notmatch [regex]::Escape($requiredText)) {
        throw "Documentation contract is missing '$requiredText'."
    }
}

$packageDirectory = Join-Path $repositoryRoot 'TestResults/package-verification'
foreach ($package in @($expectedPackages + 'Nodal.Import' + 'Nodal.Tool')) {
    $artifact = Join-Path $packageDirectory "$package.$PackageVersion.nupkg"
    if (-not (Test-Path -LiteralPath $artifact -PathType Leaf)) {
        throw "Documentation contract requires verified package '$artifact'."
    }
}

Write-Host 'Documentation and package-only reference journey are aligned.'
