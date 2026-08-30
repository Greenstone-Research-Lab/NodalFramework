param(
    [string]$PackageVersion = '0.1.0-beta.1'
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
    'Nodal.Import' = @('Nodal.Core')
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

& (Join-Path $PSScriptRoot 'normalize-nuget-packages.ps1') `
    -PackageDirectory $outputDirectory

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
                'nodal-package-icon.png'
                'tools/net10.0/any/DotnetToolSettings.xml'
                'tools/net10.0/any/Nodal.Tool.dll'
                'tools/net10.0/any/Nodal.Tool.xml'
                'tools/net10.0/any/Nodal.Migrations.dll'
                'tools/net10.0/any/Nodal.Import.Relational.dll'
                'tools/net10.0/any/Nodal.Modeling.CodeGeneration.dll'
            )
        }
        else {
            @(
                'LICENSE.txt'
                'README.md'
                'nodal-package-icon.png'
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
        $icon = $metadata.SelectSingleNode("*[local-name()='icon']").InnerText
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        $authors = $metadata.SelectSingleNode("*[local-name()='authors']").InnerText
        $description = $metadata.SelectSingleNode("*[local-name()='description']").InnerText
        $tags = $metadata.SelectSingleNode("*[local-name()='tags']").InnerText
        $projectUrl = $metadata.SelectSingleNode("*[local-name()='projectUrl']").InnerText
        $releaseNotes = $metadata.SelectSingleNode("*[local-name()='releaseNotes']").InnerText

        if ($actualId -ne $packageId -or $actualVersion -ne $PackageVersion) {
            throw "Package identity mismatch. Expected '$packageId/$PackageVersion', found '$actualId/$actualVersion'."
        }

        if ($license.type -ne 'expression' -or $license.InnerText -ne 'MPL-2.0') {
            throw "Package '$packageId' must declare the MPL-2.0 license expression."
        }

        if ($readme -ne 'README.md') {
            throw "Package '$packageId' must declare README.md as its NuGet readme."
        }

        if ($icon -ne 'nodal-package-icon.png') {
            throw "Package '$packageId' must declare the shared Nodal package icon."
        }

        if ($repository.type -ne 'git' -or
            $repository.url -ne 'https://github.com/Greenstone-Research-Lab/NodalFramework') {
            throw "Package '$packageId' has invalid repository metadata."
        }

        if ($authors -ne 'Greenstone Research Lab' -or [string]::IsNullOrWhiteSpace($description)) {
            throw "Package '$packageId' must declare its author and a non-empty description."
        }

        if ($tags -notmatch '(^|[; ])graph($|[; ])' -or $tags -notmatch '(^|[; ])nodal($|[; ])') {
            throw "Package '$packageId' must include the common graph and nodal tags."
        }

        if ($projectUrl -ne 'https://github.com/Greenstone-Research-Lab/NodalFramework' -or
            $releaseNotes -ne 'https://github.com/Greenstone-Research-Lab/NodalFramework/releases') {
            throw "Package '$packageId' has invalid project or release-notes metadata."
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
