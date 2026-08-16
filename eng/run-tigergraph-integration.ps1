$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$requiredVariables = @(
    'NODAL_TIGERGRAPH_ENDPOINT',
    'NODAL_TIGERGRAPH_ACCESS_TOKEN',
    'NODAL_TIGERGRAPH_GRAPH'
)

$missingVariables = $requiredVariables | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
}
if ($missingVariables) {
    throw "Missing TigerGraph integration variables: $($missingVariables -join ', ')"
}

dotnet test "$repositoryRoot/tests/Nodal.IntegrationTests/Nodal.IntegrationTests.csproj" `
    --configuration Release `
    --filter 'Provider=TigerGraph'
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
