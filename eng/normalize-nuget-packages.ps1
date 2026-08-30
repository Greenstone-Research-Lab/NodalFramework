param(
    [Parameter(Mandatory = $true)]
    [string]$PackageDirectory
)

$ErrorActionPreference = 'Stop'
$resolvedDirectory = (Resolve-Path -LiteralPath $PackageDirectory).Path
$fixedTimestamp = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
$fixedCorePropertiesPath = 'package/services/metadata/core-properties/core-properties.psmdcp'
$corePropertiesRelationshipType = 'http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties'
$utf8 = [System.Text.UTF8Encoding]::new($false)

Add-Type -AssemblyName System.IO.Compression.FileSystem

$packages = @(Get-ChildItem -LiteralPath $resolvedDirectory -File |
    Where-Object Extension -In '.nupkg', '.snupkg' |
    Sort-Object Name)

if ($packages.Count -eq 0) {
    throw "No NuGet package artifacts were found in '$resolvedDirectory'."
}

foreach ($package in $packages) {
    $temporaryPath = "$($package.FullName).normalized"
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }

    $source = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $corePropertiesEntries = @($source.Entries |
            Where-Object FullName -Like 'package/services/metadata/core-properties/*.psmdcp')
        if ($corePropertiesEntries.Count -ne 1) {
            throw "Package '$($package.Name)' must contain exactly one core-properties entry."
        }

        $originalCorePropertiesPath = $corePropertiesEntries[0].FullName
        $entries = [System.Collections.Generic.List[object]]::new()
        foreach ($entry in $source.Entries) {
            $stream = $entry.Open()
            try {
                $memory = [System.IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($memory)
                    $content = $memory.ToArray()
                }
                finally {
                    $memory.Dispose()
                }
            }
            finally {
                $stream.Dispose()
            }

            $entryName = if ($entry.FullName -eq $originalCorePropertiesPath) {
                $fixedCorePropertiesPath
            }
            else {
                $entry.FullName
            }

            if ($entry.FullName -eq '_rels/.rels') {
                $relationships = $utf8.GetString($content)
                $relationshipPattern = '<Relationship\s+Type="' +
                    [regex]::Escape($corePropertiesRelationshipType) +
                    '"\s+Target="[^"]+"\s+Id="[^"]+"\s*/>'
                $canonicalRelationship = '<Relationship Type="' +
                    $corePropertiesRelationshipType +
                    '" Target="/' +
                    $fixedCorePropertiesPath +
                    '" Id="R0000000000000000" />'
                $relationships = [regex]::Replace(
                    $relationships,
                    $relationshipPattern,
                    $canonicalRelationship,
                    [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
                if ($relationships -notmatch [regex]::Escape("/$fixedCorePropertiesPath")) {
                    throw "Package '$($package.Name)' has an unsupported core-properties relationship format."
                }

                $content = $utf8.GetBytes($relationships)
            }

            $entries.Add([pscustomobject]@{
                Name = $entryName
                Content = [byte[]]$content
            })
        }
    }
    finally {
        $source.Dispose()
    }

    $targetStream = [System.IO.File]::Open(
        $temporaryPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::None)
    try {
        $target = [System.IO.Compression.ZipArchive]::new(
            $targetStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            foreach ($entry in @($entries | Sort-Object Name)) {
                $targetEntry = $target.CreateEntry(
                    $entry.Name,
                    [System.IO.Compression.CompressionLevel]::Optimal)
                $targetEntry.LastWriteTime = $fixedTimestamp
                $targetEntry.ExternalAttributes = 0
                $targetEntryStream = $targetEntry.Open()
                try {
                    $targetEntryStream.Write($entry.Content, 0, $entry.Content.Length)
                }
                finally {
                    $targetEntryStream.Dispose()
                }
            }
        }
        finally {
            $target.Dispose()
        }
    }
    finally {
        $targetStream.Dispose()
    }

    Move-Item -LiteralPath $temporaryPath -Destination $package.FullName -Force
}

Write-Host "Normalized $($packages.Count) NuGet package artifacts for deterministic publication."
