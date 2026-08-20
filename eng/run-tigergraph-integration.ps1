$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$requiredVariables = @(
    'NODAL_TIGERGRAPH_ENDPOINT',
    'NODAL_TIGERGRAPH_GRAPH'
)

$missingVariables = $requiredVariables | Where-Object {
    [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_))
}
if ($missingVariables) {
    throw "Missing TigerGraph integration variables: $($missingVariables -join ', ')"
}

$hasToken = -not [string]::IsNullOrWhiteSpace($env:NODAL_TIGERGRAPH_ACCESS_TOKEN)
$hasCredentials = -not [string]::IsNullOrWhiteSpace($env:NODAL_TIGERGRAPH_USERNAME) -and
    $null -ne $env:NODAL_TIGERGRAPH_PASSWORD
if (-not $hasToken -and -not $hasCredentials) {
    throw 'TigerGraph integration requires an access token or a username and password.'
}

dotnet test "$repositoryRoot/tests/Nodal.IntegrationTests/Nodal.IntegrationTests.csproj" `
    --configuration Release `
    --filter 'Provider=TigerGraph'
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}
