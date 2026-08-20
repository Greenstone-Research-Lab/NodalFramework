param(
    [switch]$SkipTigerGraph
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'compose.local.yml'

function Invoke-Docker {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    & docker @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Docker command failed: docker $($Arguments -join ' ')"
    }
}

function Wait-ForHealthyContainer {
    param(
        [string]$ContainerName,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $state = docker inspect `
            --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' `
            $ContainerName 2>$null
        if ($LASTEXITCODE -eq 0 -and $state -eq 'healthy') {
            Write-Host "$ContainerName is healthy."
            return
        }

        if ($state -eq 'exited' -or $state -eq 'dead') {
            docker logs --tail 100 $ContainerName
            throw "$ContainerName stopped before becoming healthy."
        }

        Start-Sleep -Seconds 3
    }
    while ([DateTimeOffset]::UtcNow -lt $deadline)

    docker logs --tail 100 $ContainerName
    throw "$ContainerName did not become healthy within $TimeoutSeconds seconds."
}

Invoke-Docker compose --file $composeFile up --detach neo4j
Wait-ForHealthyContainer -ContainerName 'nodal-neo4j' -TimeoutSeconds 120

if (-not $SkipTigerGraph) {
    Invoke-Docker compose --file $composeFile --profile tigergraph up --detach tigergraph
    Wait-ForHealthyContainer -ContainerName 'nodal-tigergraph' -TimeoutSeconds 300

    $schema = docker exec --user tigergraph nodal-tigergraph `
        /home/tigergraph/tigergraph/app/cmd/gsql ls
    if ($LASTEXITCODE -ne 0) {
        throw 'TigerGraph schema could not be inspected.'
    }

    $schemaText = $schema -join [Environment]::NewLine
    if ($schemaText -notmatch '(?m)^\s*-\s+Graph\s+NodalQa\b') {
        Invoke-Docker exec --user tigergraph nodal-tigergraph `
            /home/tigergraph/tigergraph/app/cmd/gsql `
            /home/tigergraph/nodal-init/init.gsql
    }
}

Write-Host ''
Write-Host 'Local graph databases are ready:'
Write-Host '  Neo4j Browser : http://localhost:7474'
Write-Host '  Neo4j Bolt    : neo4j://localhost:7687'
Write-Host '  Neo4j login   : neo4j / NodalLocal123!'
if (-not $SkipTigerGraph) {
    Write-Host '  TigerGraph    : http://localhost:14240'
    Write-Host '  TigerGraph QA : NodalQa / tigergraph / tigergraph'
}
