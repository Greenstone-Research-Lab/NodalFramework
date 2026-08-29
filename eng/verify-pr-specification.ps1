param(
    [AllowEmptyString()] [string]$PullRequestBody
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

if ([string]::IsNullOrWhiteSpace($PullRequestBody)) {
    throw 'The pull request body must contain a Specification reference.'
}

$referenceLine = @($PullRequestBody -split '\r?\n' | Where-Object { $_ -match '^Specification:\s*' })
if ($referenceLine.Count -ne 1) {
    throw 'The pull request body must contain exactly one line beginning with Specification:.'
}

$reference = ($referenceLine[0] -replace '^Specification:\s*', '').Trim()
if ($reference -match '^N/A\s*-\s*(?<reason>.{10,})$') {
    Write-Host "Accepted non-behavioral specification exemption: $($Matches.reason)"
    return
}

if ($reference -notmatch '^[A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+(?:\s*,\s*[A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+)*$') {
    throw 'Specification must list one or more comma-separated identifiers, or N/A - followed by a meaningful reason.'
}

$knownIdentifiers = @{}
Get-ChildItem -LiteralPath (Join-Path $root 'specs') -Filter '*.md' -File -Recurse |
    Where-Object { $_.FullName -notmatch '[\\/]templates[\\/]' } |
    ForEach-Object {
        $content = Get-Content -LiteralPath $_.FullName -Raw
        if ($content -match '(?m)^id:\s*(?<id>[A-Z][A-Z0-9]+(?:-[A-Z0-9]+)+)\s*$') {
            $knownIdentifiers[$Matches.id] = $true
        }
    }

foreach ($identifier in ($reference -split '\s*,\s*')) {
    if (-not $knownIdentifiers.ContainsKey($identifier)) {
        throw "Pull request references unknown specification '$identifier'."
    }
}

Write-Host "Verified pull request specification reference: $reference"
