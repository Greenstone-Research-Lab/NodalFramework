$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

& "$PSScriptRoot/start-local-databases.ps1"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run `
    --project "$repositoryRoot/samples/Nodal.Samples.Neo4j/Nodal.Samples.Neo4j.csproj" `
    --configuration Release
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet run `
    --project "$repositoryRoot/samples/Nodal.Samples.TigerGraph/Nodal.Samples.TigerGraph.csproj" `
    --configuration Release
exit $LASTEXITCODE
