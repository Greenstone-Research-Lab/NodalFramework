$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot/start-local-databases.ps1"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

$env:NODAL_NEO4J_ENDPOINT = 'neo4j://localhost:7687'
$env:NODAL_NEO4J_USERNAME = 'neo4j'
$env:NODAL_NEO4J_PASSWORD = 'NodalLocal123!'
$env:NODAL_NEO4J_DATABASE = 'neo4j'
$env:NODAL_TIGERGRAPH_ENDPOINT = 'http://localhost:14240/'
$env:NODAL_TIGERGRAPH_ACCESS_TOKEN = $null
$env:NODAL_TIGERGRAPH_USERNAME = 'tigergraph'
$env:NODAL_TIGERGRAPH_PASSWORD = 'tigergraph'
$env:NODAL_TIGERGRAPH_GRAPH = 'NodalQa'

dotnet test "$repositoryRoot/tests/Nodal.IntegrationTests/Nodal.IntegrationTests.csproj" `
    --configuration Release
exit $LASTEXITCODE
