param(
    [string]$Image = 'neo4j:5.26-community'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$containerName = "nodal-neo4j-$([guid]::NewGuid().ToString('N'))"
$password = "Nodal-$([guid]::NewGuid().ToString('N'))"

try {
    docker run --detach --rm --name $containerName --publish-all `
        --env "NEO4J_AUTH=neo4j/$password" $Image | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'Docker could not start the Neo4j integration container.'
    }

    $port = $null
    for ($attempt = 0; $attempt -lt 60 -and -not $port; $attempt++) {
        Start-Sleep -Seconds 1
        $binding = docker port $containerName '7687/tcp' 2>$null | Select-Object -First 1
        if ($binding -match ':(?<port>\d+)$') {
            $port = $Matches.port
        }
    }

    if (-not $port) {
        throw 'Neo4j Bolt port was not published before the timeout.'
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 90 -and -not $ready; $attempt++) {
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = 'SilentlyContinue'
        docker exec $containerName cypher-shell `
            -u neo4j `
            -p $password `
            'RETURN 1;' 2>$null | Out-Null
        $ready = $LASTEXITCODE -eq 0
        $ErrorActionPreference = $previousErrorActionPreference
        if (-not $ready) {
            Start-Sleep -Seconds 1
        }
    }

    if (-not $ready) {
        throw 'Neo4j did not accept a Cypher readiness query before the timeout.'
    }

    $env:NODAL_NEO4J_ENDPOINT = "neo4j://localhost:$port"
    $env:NODAL_NEO4J_USERNAME = 'neo4j'
    $env:NODAL_NEO4J_PASSWORD = $password
    $env:NODAL_NEO4J_DATABASE = 'neo4j'

    dotnet test "$repositoryRoot/tests/Nodal.IntegrationTests/Nodal.IntegrationTests.csproj" `
        --configuration Release `
        --filter 'Provider=Neo4j'
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}
finally {
    docker rm --force $containerName 2>$null | Out-Null
}
