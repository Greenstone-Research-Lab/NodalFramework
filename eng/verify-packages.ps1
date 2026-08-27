param(
    [string]$PackageVersion = '0.1.0-alpha.1'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputDirectory = Join-Path $repositoryRoot 'TestResults/package-verification'
$expectedPackages = @{
    'Nodal.Core' = @()
    'Nodal.Migrations' = @('Nodal.Core')
    'Nodal.Neo4j' = @('Nodal.Core', 'Neo4j.Driver')
    'Nodal.Analytics' = @('Nodal.Core')
    'Nodal.TigerGraph' = @('Nodal.Core')
    'Nodal.Tool' = @()
    'Nodal.Import' = @()
    'Nodal.Import.Csv' = @('Nodal.Import')
    'Nodal.Import.Relational' = @('Nodal.Import')
}

if (Test-Path -LiteralPath $outputDirectory) {
    Remove-Item -LiteralPath $outputDirectory -Recurse -Force
}

New-Item -ItemType Directory -Path $outputDirectory | Out-Null

Write-Host 'Validating public package API compatibility against the approved NuGet baseline.'
dotnet pack "$repositoryRoot/Nodal.slnx" `
    --configuration Release `
    --no-restore `
    --output $outputDirectory `
    -p:PackageVersion=$PackageVersion
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = @(Get-ChildItem -LiteralPath $outputDirectory -Filter '*.nupkg')
$symbolPackages = @(Get-ChildItem -LiteralPath $outputDirectory -Filter '*.snupkg')

if ($packages.Count -ne $expectedPackages.Count) {
    throw "Expected $($expectedPackages.Count) NuGet packages but found $($packages.Count)."
}

if ($symbolPackages.Count -ne $expectedPackages.Count) {
    throw "Expected $($expectedPackages.Count) symbol packages but found $($symbolPackages.Count)."
}

foreach ($packageId in $expectedPackages.Keys) {
    $packagePath = Join-Path $outputDirectory "$packageId.$PackageVersion.nupkg"
    $symbolPath = Join-Path $outputDirectory "$packageId.$PackageVersion.snupkg"

    if (-not (Test-Path -LiteralPath $packagePath)) {
        throw "Package '$packageId' was not produced at the expected version."
    }

    if (-not (Test-Path -LiteralPath $symbolPath)) {
        throw "Symbol package for '$packageId' was not produced."
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($packagePath)
    try {
        $entryNames = @($archive.Entries | ForEach-Object FullName)
        $requiredEntries = if ($packageId -eq 'Nodal.Tool') {
            @(
                'LICENSE.txt'
                'README.md'
                'tools/net10.0/any/DotnetToolSettings.xml'
                'tools/net10.0/any/Nodal.Tool.dll'
                'tools/net10.0/any/Nodal.Tool.xml'
                'tools/net10.0/any/Nodal.Migrations.dll'
            )
        }
        else {
            @(
                'LICENSE.txt'
                'README.md'
                "lib/net10.0/$packageId.dll"
                "lib/net10.0/$packageId.xml"
            )
        }

        foreach ($requiredEntry in $requiredEntries) {
            if ($requiredEntry -notin $entryNames) {
                throw "Package '$packageId' does not contain '$requiredEntry'."
            }
        }

        $nuspecEntry = $archive.Entries |
            Where-Object FullName -Like '*.nuspec' |
            Select-Object -First 1
        if ($null -eq $nuspecEntry) {
            throw "Package '$packageId' does not contain a nuspec manifest."
        }

        $reader = [System.IO.StreamReader]::new($nuspecEntry.Open())
        try {
            [xml]$nuspec = $reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }

        $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        $actualId = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        $actualVersion = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        $license = $metadata.SelectSingleNode("*[local-name()='license']")
        $readme = $metadata.SelectSingleNode("*[local-name()='readme']").InnerText
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")

        if ($actualId -ne $packageId -or $actualVersion -ne $PackageVersion) {
            throw "Package identity mismatch. Expected '$packageId/$PackageVersion', found '$actualId/$actualVersion'."
        }

        if ($license.type -ne 'expression' -or $license.InnerText -ne 'MPL-2.0') {
            throw "Package '$packageId' must declare the MPL-2.0 license expression."
        }

        if ($readme -ne 'README.md') {
            throw "Package '$packageId' must declare README.md as its NuGet readme."
        }

        if ($repository.type -ne 'git' -or
            $repository.url -ne 'https://github.com/Greenstone-Research-Lab/NodalFramework') {
            throw "Package '$packageId' has invalid repository metadata."
        }

        $actualDependencies = @(
            $metadata.SelectNodes("*[local-name()='dependencies']/*[local-name()='group']/*[local-name()='dependency']") |
                ForEach-Object { $_.id }
        )
        foreach ($dependency in $expectedPackages[$packageId]) {
            if ($dependency -notin $actualDependencies) {
                throw "Package '$packageId' is missing dependency '$dependency'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Host "Verified $($packages.Count) NuGet packages and $($symbolPackages.Count) symbol packages at version $PackageVersion."
