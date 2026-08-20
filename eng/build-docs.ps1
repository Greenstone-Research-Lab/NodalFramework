[CmdletBinding()]
param(
    [switch]$SkipDotNetRestore,
    [switch]$SkipNodeInstall
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$websitePath = Join-Path $repositoryRoot 'website'
$npmCommand = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'npm.cmd' } else { 'npm' }

Push-Location $repositoryRoot
try {
    if (-not $SkipDotNetRestore) {
        dotnet restore Nodal.slnx
        if ($LASTEXITCODE -ne 0) { throw 'The .NET restore failed.' }
    }

    dotnet build Nodal.slnx --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'The documentation source build failed.' }

    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'The local .NET tool restore failed.' }

    dotnet tool run docfx -- docs/api/docfx.json
    if ($LASTEXITCODE -ne 0) { throw 'DocFX API generation failed.' }

    $docfxIndexPath = Join-Path $websitePath 'static/api/index.html'
    $docfxIndex = [System.IO.File]::ReadAllText($docfxIndexPath)
    $headPattern = [regex]::new('<head>\s*')
    if (-not $headPattern.IsMatch($docfxIndex)) {
        throw 'The generated DocFX index does not contain a head element.'
    }

    $docfxIndex = $headPattern.Replace(
        $docfxIndex,
        "<head>`n    <base href=`"/api/`">`n    ",
        1)
    [System.IO.File]::WriteAllText(
        $docfxIndexPath,
        $docfxIndex,
        [System.Text.UTF8Encoding]::new($false))

    if (-not $SkipNodeInstall) {
        & $npmCommand ci --prefix $websitePath
        if ($LASTEXITCODE -ne 0) { throw 'The documentation npm restore failed.' }
    }

    & $npmCommand run typecheck --prefix $websitePath
    if ($LASTEXITCODE -ne 0) { throw 'The documentation type check failed.' }

    & $npmCommand run build --prefix $websitePath
    if ($LASTEXITCODE -ne 0) { throw 'The Docusaurus production build failed.' }
}
finally {
    Pop-Location
}
