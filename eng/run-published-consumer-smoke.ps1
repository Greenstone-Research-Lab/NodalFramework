param(
    [Parameter(Mandatory)] [string]$PackageVersion,
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json',
    [string]$AdditionalPackageSource,
    [int]$RestoreAttempts = 40,
    [int]$RetryDelaySeconds = 30
)

$ErrorActionPreference = 'Stop'
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'PackageVersion must be a valid immutable NuGet version.' }
if ($RestoreAttempts -lt 1) { throw 'RestoreAttempts must be at least one.' }
if ($RetryDelaySeconds -lt 1) { throw 'RetryDelaySeconds must be at least one.' }
$root = Split-Path -Parent $PSScriptRoot
$template = Join-Path $root 'consumer-smoke/WorldFoodDelivery'
$workspace = Join-Path ([System.IO.Path]::GetTempPath()) ("nodal-clean-room-" + [Guid]::NewGuid().ToString('N'))
$packages = Join-Path $workspace 'packages'
try {
    New-Item -ItemType Directory -Path $workspace | Out-Null
    Copy-Item -Path (Join-Path $template '*') -Destination $workspace -Recurse
    $additionalSourceXml = if ([string]::IsNullOrWhiteSpace($AdditionalPackageSource)) { '' } else { "<add key=`"additional-feed`" value=`"$AdditionalPackageSource`" />" }
    @"
<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="published-nuget" value="$PackageSource" />$additionalSourceXml</packageSources></configuration>
"@ | Set-Content -LiteralPath (Join-Path $workspace 'NuGet.Config') -NoNewline
    $project = Join-Path $workspace 'WorldFoodDelivery.csproj'
    $restoreStartedAtUtc = [DateTimeOffset]::UtcNow
    for ($attempt = 1; $attempt -le $RestoreAttempts; $attempt++) {
        Write-Host "Published-package restore attempt $attempt of $RestoreAttempts for Nodal $PackageVersion."
        dotnet restore $project `
            --configfile (Join-Path $workspace 'NuGet.Config') `
            --packages $packages `
            --no-cache `
            --force-evaluate `
            -p:NodalPackageVersion=$PackageVersion
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $RestoreAttempts) {
            throw "Could not restore published Nodal $PackageVersion after $RestoreAttempts attempts. NuGet.org may still be indexing the newly published version."
        }
        Start-Sleep -Seconds $RetryDelaySeconds
    }
    if (Select-String -Path $project -Pattern '<ProjectReference' -Quiet) { throw 'Clean-room consumer projects must not contain ProjectReference entries.' }
    $assets = Get-Content (Join-Path $workspace 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $nodalLibraries = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Nodal\.' })
    $expectedPackages = @(
        'Nodal.Core',
        'Nodal.Import',
        'Nodal.Import.Csv',
        'Nodal.Import.Relational',
        'Nodal.Migrations',
        'Nodal.Neo4j',
        'Nodal.TigerGraph'
    )
    foreach ($id in $expectedPackages) { if ($nodalLibraries -notcontains "$id/$PackageVersion") { throw "Expected published package '$id/$PackageVersion' was not restored." } }
    dotnet run --project $project --no-restore -p:NodalPackageVersion=$PackageVersion -- `
        (Join-Path $workspace 'orders.csv') `
        (Join-Path $workspace 'artifacts')
    if ($LASTEXITCODE -ne 0) { throw 'The clean-room consumer execution failed.' }
    $evidence = [ordered]@{
        packageVersion = $PackageVersion
        packageSource = $PackageSource
        packages = $nodalLibraries
        restoreAttempts = $attempt
        indexingWaitSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $restoreStartedAtUtc).TotalSeconds, 2)
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json
    $evidenceDirectory = Join-Path $root 'TestResults'
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidence | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'published-consumer-smoke.json')
}
finally { if (Test-Path $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force } }
