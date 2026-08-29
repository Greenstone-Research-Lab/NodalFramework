param(
    [string]$SpecificationRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'specs')
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $SpecificationRoot -PathType Container)) {
    throw "Specification root '$SpecificationRoot' does not exist."
}

$allowedStatuses = @('draft', 'accepted', 'implemented', 'superseded', 'rejected')
$allowedTypes = @('feature', 'provider-capability', 'decision')
$requiredSections = @{
    'feature' = @(
        'Objective and user value',
        'Non-goals',
        'Terminology and invariants',
        'API and usage examples',
        'Provider and version scope',
        'Architecture and dependencies',
        'Operational behavior',
        'Security and privacy',
        'Verification strategy',
        'Performance budget',
        'Delivery impact',
        'Acceptance criteria and evidence'
    )
    'provider-capability' = @(
        'Objective and user value',
        'Capability contract',
        'Provider and version scope',
        'Portable and provider-specific behavior',
        'Failure semantics',
        'Verification strategy',
        'Performance budget',
        'Acceptance criteria and evidence'
    )
    'decision' = @('Context', 'Decision', 'Consequences', 'Alternatives considered')
}

$documents = @(Get-ChildItem -LiteralPath $SpecificationRoot -Filter '*.md' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]templates[\\/]' -and $_.Name -ne 'README.md' })

if ($documents.Count -eq 0) { throw 'At least one governed specification document is required.' }

$seenIdentifiers = @{}
foreach ($document in $documents) {
    $content = Get-Content -LiteralPath $document.FullName -Raw
    if ($content -notmatch '(?s)^---\r?\n(?<metadata>.*?)\r?\n---\r?\n') {
        throw "Specification '$($document.FullName)' must start with YAML-style metadata delimiters."
    }

    $metadata = @{}
    foreach ($line in ($Matches.metadata -split '\r?\n')) {
        if ($line -match '^(?<key>[a-z-]+):\s*(?<value>.+?)\s*$') {
            $metadata[$Matches.key] = $Matches.value.Trim('"', "'")
        }
    }

    foreach ($key in @('id', 'title', 'status', 'type', 'owners', 'last-reviewed')) {
        if (-not $metadata.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($metadata[$key])) {
            throw "Specification '$($document.FullName)' is missing metadata '$key'."
        }
    }

    $identifier = $metadata.id
    if ($identifier -notmatch '^[A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+$') {
        throw "Specification '$($document.FullName)' has invalid id '$identifier'."
    }
    if ($seenIdentifiers.ContainsKey($identifier)) {
        throw "Specification id '$identifier' is duplicated by '$($document.FullName)' and '$($seenIdentifiers[$identifier])'."
    }
    $seenIdentifiers[$identifier] = $document.FullName

    if ($allowedStatuses -notcontains $metadata.status) {
        throw "Specification '$identifier' has unsupported status '$($metadata.status)'."
    }
    if ($allowedTypes -notcontains $metadata.type) {
        throw "Specification '$identifier' has unsupported type '$($metadata.type)'."
    }

    $reviewDate = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($metadata['last-reviewed'], 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$reviewDate)) {
        throw "Specification '$identifier' must use yyyy-MM-dd for last-reviewed."
    }

    foreach ($section in $requiredSections[$metadata.type]) {
        if ($content -notmatch "(?m)^## $([Regex]::Escape($section))\s*$") {
            throw "Specification '$identifier' is missing required section '## $section'."
        }
    }

    if ($metadata.status -in @('superseded', 'rejected') -and $content -notmatch '(?m)^superseded-by:\s*\S+|^rejection-reason:\s*\S+') {
        throw "Specification '$identifier' must record replacement or rejection metadata."
    }
}

Write-Host "Verified $($documents.Count) governed specification document(s)."
