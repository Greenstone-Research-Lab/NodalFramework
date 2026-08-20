param(
    [string]$PackageVersion = '0.1.0-alpha.1'
)

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

& "$PSScriptRoot/verify-coverage.ps1"
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& "$PSScriptRoot/verify-packages.ps1" -PackageVersion $PackageVersion
exit $LASTEXITCODE
