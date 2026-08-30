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
    $toolPath = Join-Path $workspace 'tools'
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
        'Nodal.Analytics',
        'Nodal.Import',
        'Nodal.Import.Csv',
        'Nodal.Import.Relational',
        'Nodal.Migrations',
        'Nodal.Neo4j',
        'Nodal.TigerGraph'
    )
    foreach ($id in $expectedPackages) { if ($nodalLibraries -notcontains "$id/$PackageVersion") { throw "Expected published package '$id/$PackageVersion' was not restored." } }
    dotnet tool install Nodal.Tool `
        --tool-path $toolPath `
        --version $PackageVersion `
        --configfile (Join-Path $workspace 'NuGet.Config') `
        --add-source $PackageSource
    if ($LASTEXITCODE -ne 0) { throw 'The published Nodal.Tool package could not be installed.' }
    dotnet run --project $project --no-restore -p:NodalPackageVersion=$PackageVersion -- `
        (Join-Path $workspace 'orders.csv') `
        (Join-Path $workspace 'artifacts')
    if ($LASTEXITCODE -ne 0) { throw 'The clean-room consumer execution failed.' }
    $toolExecutable = Join-Path $toolPath $(if ($IsWindows) { 'nodal.exe' } else { 'nodal' })
    $descriptor = Join-Path $workspace 'artifacts/world-food-delivery.nodal.json'
    $generated = Join-Path $workspace 'Generated'
    & $toolExecutable model validate --descriptor $descriptor
    if ($LASTEXITCODE -ne 0) { throw 'The generated relational descriptor failed validation.' }
    & $toolExecutable model inspect --descriptor $descriptor --format json --output (Join-Path $workspace 'artifacts/model-inspection.json')
    if ($LASTEXITCODE -ne 0) { throw 'The descriptor inspection failed.' }
    & $toolExecutable model generate --descriptor $descriptor --output $generated --namespace 'WorldFoodDelivery.Generated' --context 'GeneratedFoodDeliveryContext'
    if ($LASTEXITCODE -ne 0) { throw 'Strong-type generation failed.' }
    dotnet build $project --no-restore -p:NodalPackageVersion=$PackageVersion
    if ($LASTEXITCODE -ne 0) { throw 'The isolated consumer did not compile with generated strong types.' }
    $evolvedDescriptor = Join-Path $workspace 'artifacts/world-food-delivery-v2.nodal.json'
    $evolved = Get-Content -LiteralPath $descriptor -Raw | ConvertFrom-Json
    $firstNode = @($evolved.nodes)[0]
    $firstNode.properties += [pscustomobject]@{
        name = 'nodal_review_note'
        clrName = 'NodalReviewNote'
        valueKind = 'Text'
        isNullable = $true
        isCollection = $false
        itemKind = $null
        providerAnnotations = $null
    }
    $evolved | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $evolvedDescriptor
    & $toolExecutable model diff --from $descriptor --to $evolvedDescriptor --format json --output (Join-Path $workspace 'artifacts/model-diff.json')
    if ($LASTEXITCODE -ne 0) { throw 'The additive schema evolution diff failed.' }
    & $toolExecutable model generate --descriptor $evolvedDescriptor --output (Join-Path $workspace 'obj/GeneratedV2') --namespace 'WorldFoodDelivery.GeneratedV2' --context 'GeneratedFoodDeliveryV2Context'
    if ($LASTEXITCODE -ne 0) { throw 'Schema evolution regeneration failed.' }
    $evidence = [ordered]@{
        packageVersion = $PackageVersion
        packageSource = $PackageSource
        packages = $nodalLibraries
        generatedFiles = @(Get-ChildItem -LiteralPath $generated -Recurse -Filter '*.cs').Count
        descriptorFingerprint = (Get-Content -LiteralPath (Join-Path $workspace 'artifacts/model-inspection.json') -Raw | ConvertFrom-Json).fingerprint
        restoreAttempts = $attempt
        indexingWaitSeconds = [Math]::Round(([DateTimeOffset]::UtcNow - $restoreStartedAtUtc).TotalSeconds, 2)
        verifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    } | ConvertTo-Json
    $evidenceDirectory = Join-Path $root 'TestResults'
    New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null
    $evidence | Set-Content -LiteralPath (Join-Path $evidenceDirectory 'published-consumer-smoke.json')
}
finally { if (Test-Path $workspace) { Remove-Item -LiteralPath $workspace -Recurse -Force } }
