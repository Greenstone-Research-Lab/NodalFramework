$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'compose.local.yml'

docker compose --file $composeFile --profile tigergraph down
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host 'Local graph database containers stopped. Persistent volumes were preserved.'
