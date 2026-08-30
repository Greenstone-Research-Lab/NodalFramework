param(
    [string]$PackageVersion = '0.1.0-beta.1',
    [string]$OutputDirectory = 'TestResults/reproducible-packages'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$destination = Join-Path $repositoryRoot $OutputDirectory
$first = Join-Path $destination 'first'
$second = Join-Path $destination 'second'

if (Test-Path -LiteralPath $destination) {
    Remove-Item -LiteralPath $destination -Recurse -Force
}
New-Item -ItemType Directory -Path $first, $second -Force | Out-Null

foreach ($output in @($first, $second)) {
    dotnet pack (Join-Path $repositoryRoot 'Nodal.slnx') `
        --configuration Release `
        --no-build `
        --no-restore `
        --output $output `
        -p:PackageVersion=$PackageVersion
    if ($LASTEXITCODE -ne 0) {
        throw "Reproducibility pack failed for '$output'."
    }

    & (Join-Path $PSScriptRoot 'normalize-nuget-packages.ps1') `
        -PackageDirectory $output
}

$firstArtifacts = @(Get-ChildItem -LiteralPath $first -File |
    Where-Object Extension -In '.nupkg', '.snupkg' |
    Sort-Object Name)
$secondArtifacts = @(Get-ChildItem -LiteralPath $second -File |
    Where-Object Extension -In '.nupkg', '.snupkg' |
    Sort-Object Name)

if ($firstArtifacts.Count -ne 18 -or $secondArtifacts.Count -ne 18) {
    throw "Expected 18 package artifacts per build; found $($firstArtifacts.Count) and $($secondArtifacts.Count)."
}

$results = [System.Collections.Generic.List[object]]::new()
foreach ($artifact in $firstArtifacts) {
    $comparison = Join-Path $second $artifact.Name
    if (-not (Test-Path -LiteralPath $comparison -PathType Leaf)) {
        throw "Second package build is missing '$($artifact.Name)'."
    }

    $firstHash = (Get-FileHash -LiteralPath $artifact.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $secondHash = (Get-FileHash -LiteralPath $comparison -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($firstHash -ne $secondHash) {
        throw "Package '$($artifact.Name)' is not byte-for-byte reproducible."
    }

    $results.Add([ordered]@{
        file = $artifact.Name
        sha256 = $firstHash
        size = $artifact.Length
    })
}

$report = [ordered]@{
    schemaVersion = 1
    packageVersion = $PackageVersion
    artifactCount = $results.Count
    reproducible = $true
    artifacts = $results
}
$report | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath (Join-Path $destination 'reproducibility.json') -Encoding utf8

Write-Host "Verified $($results.Count) byte-for-byte reproducible package artifacts."
