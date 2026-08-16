$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot

dotnet restore "$repositoryRoot/Nodal.slnx"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet format "$repositoryRoot/Nodal.slnx" --no-restore --verify-no-changes
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet build "$repositoryRoot/Nodal.slnx" --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

dotnet test "$repositoryRoot/Nodal.slnx" --configuration Release --no-build
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& "$PSScriptRoot/verify-core-coverage.ps1"
exit $LASTEXITCODE
