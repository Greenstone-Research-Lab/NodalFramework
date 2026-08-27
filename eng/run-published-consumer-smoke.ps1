param(
    [Parameter(Mandatory)] [string]$PackageVersion,
    [string]$PackageSource = 'https://api.nuget.org/v3/index.json',
    [string]$AdditionalPackageSource,
    [int]$RestoreAttempts = 6
)

$ErrorActionPreference = 'Stop'
if ($PackageVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.-]+)?$') { throw 'PackageVersion must be a valid immutable NuGet version.' }
if ($RestoreAttempts -lt 1) { throw 'RestoreAttempts must be at least one.' }
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
    for ($attempt = 1; $attempt -le $RestoreAttempts; $attempt++) {
        dotnet restore $project --configfile (Join-Path $workspace 'NuGet.Config') --packages $packages -p:NodalPackageVersion=$PackageVersion
        if ($LASTEXITCODE -eq 0) { break }
        if ($attempt -eq $RestoreAttempts) { throw "Could not restore published Nodal $PackageVersion after $RestoreAttempts attempts." }
        Start-Sleep -Seconds ([Math]::Min(30, $attempt * 5))
    }
    if (Select-String -Path $project -Pattern '<ProjectReference' -Quiet) { throw 'Clean-room consumer projects must not contain ProjectReference entries.' }
    $assets = Get-Content (Join-Path $workspace 'obj/project.assets.json') -Raw | ConvertFrom-Json
    $nodalLibraries = @($assets.libraries.PSObject.Properties.Name | Where-Object { $_ -match '^Nodal\.' })
    foreach ($id in 'Nodal.Core', 'Nodal.Migrations', 'Nodal.Neo4j', 'Nodal.TigerGraph') { if ($nodalLibraries -notcontains "$id/$PackageVersion") { throw "Expected published package '$id/$PackageVersion' was not restored." } }
    dotnet run --project $project --no-restore -p:NodalPackageVersion=$PackageVersion -- (Join-Path $workspace 'orders.csv')
    if ($LASTEXITCODE -ne 0) { throw 'The clean-room consumer execution failed.' }
    $evidence = [ordered]@{ packageVersion = $PackageVersion; packageSource = $PackageSource; packages = $nodalLibraries; verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json
    $evidence | Set-Content -LiteralPath (Join-Path $root 'TestResults/published-consumer-smoke.json')
}
finally { if (Test-Path $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force } }
