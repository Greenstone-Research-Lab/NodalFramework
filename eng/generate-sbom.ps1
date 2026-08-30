param(
    [Parameter(Mandatory = $true)]
    [string]$PackageVersion,

    [string]$PackageDirectory = 'TestResults/package-verification',

    [string]$OutputDirectory = 'TestResults/release-evidence/sbom'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolVersion = '4.1.5'

if ($IsWindows) {
    $assetName = 'sbom-tool-win-x64.exe'
    $expectedHash = '625767b371b7fdd58f40f618b8a86da0247a33c89e419039c86b4edba1dad4b5'
}
elseif ($IsLinux) {
    $assetName = 'sbom-tool-linux-x64'
    $expectedHash = 'bf5d4f99bc98c119d549d08fc02ae92598a7a42772f17317c01031a92632e05b'
}
else {
    throw 'SBOM generation currently supports Windows x64 and Linux x64 runners.'
}

$packagePath = Join-Path $repositoryRoot $PackageDirectory
if (-not (Test-Path -LiteralPath $packagePath -PathType Container)) {
    throw "Package directory '$packagePath' was not found."
}
$sourcePath = Join-Path $repositoryRoot 'src'
if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
    throw "Product source directory '$sourcePath' was not found."
}

$destination = Join-Path $repositoryRoot $OutputDirectory
if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$toolPath = Join-Path ([System.IO.Path]::GetTempPath()) "nodal-$assetName"
$downloadUrl = "https://github.com/microsoft/sbom-tool/releases/download/v$toolVersion/$assetName"

try {
    Invoke-WebRequest -Uri $downloadUrl -OutFile $toolPath
    $actualHash = (Get-FileHash -LiteralPath $toolPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "SBOM tool integrity verification failed. Expected '$expectedHash', found '$actualHash'."
    }

    if ($IsLinux) {
        & chmod '+x' $toolPath
        if ($LASTEXITCODE -ne 0) {
            throw 'The SBOM tool could not be marked executable.'
        }
    }

    & $toolPath generate `
        -b $packagePath `
        -bc $sourcePath `
        -pn 'Nodal Framework packages' `
        -pv $PackageVersion `
        -ps 'Greenstone Research Lab' `
        -nsb 'https://nodalframework.pages.dev/sbom' `
        -m $destination `
        -V Information
    if ($LASTEXITCODE -ne 0) {
        throw "SBOM generation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $toolPath -Force -ErrorAction SilentlyContinue
}

$manifest = Join-Path $destination '_manifest/spdx_2.2/manifest.spdx.json'
if (-not (Test-Path -LiteralPath $manifest -PathType Leaf)) {
    throw "SBOM manifest '$manifest' was not produced."
}

Write-Host "SPDX SBOM generated at '$manifest'."
