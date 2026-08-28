[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$composeFile = Join-Path $repositoryRoot 'tests/p05/docker-compose.yml'
$outputRoot = Join-Path $repositoryRoot 'artifacts/p05-integration'
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = [IO.Path]::GetFullPath($outputRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$project = "cp6-p05-$PID"

if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write P05 evidence outside $artifactsRoot."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

function Invoke-DockerCompose {
    param([Parameter(ValueFromRemainingArguments = $true)][string[]]$Arguments)

    & docker compose --project-name $project --file $composeFile @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Get-PublishedPort {
    param([Parameter(Mandatory = $true)][string]$Service)

    $binding = (& docker compose --project-name $project --file $composeFile port $Service 8080).Trim()
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch ':(?<port>[0-9]+)$') {
        throw "Could not resolve the published port for $Service from '$binding'."
    }

    return [int]$Matches.port
}

function Invoke-WithRetry {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Action,
        [Parameter(Mandatory = $true)][string]$Description,
        [int]$Attempts = 60
    )

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            return & $Action
        } catch {
            if ($attempt -eq $Attempts) {
                throw "$Description did not succeed after $Attempts attempts: $($_.Exception.Message)"
            }
            Start-Sleep -Seconds 1
        }
    }
}

& docker version --format '{{.Server.Version}}' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'A running Docker engine is required for the P05 real integration profile.'
}

$startedAt = [DateTimeOffset]::UtcNow
try {
    Invoke-DockerCompose up --detach --build

    $publisherPort = Get-PublishedPort -Service publisher
    $receiverPort = Get-PublishedPort -Service receiver
    $publisherBase = "http://127.0.0.1:$publisherPort"
    $receiverBase = "http://127.0.0.1:$receiverPort"

    Invoke-WithRetry -Description 'Publisher health' -Action {
        Invoke-RestMethod -Uri "$publisherBase/healthz" -TimeoutSec 3 | Out-Null
    } | Out-Null
    Invoke-WithRetry -Description 'Receiver health' -Action {
        Invoke-RestMethod -Uri "$receiverBase/healthz" -TimeoutSec 3 | Out-Null
    } | Out-Null

    $invocation = Invoke-WithRetry -Description 'Dapr service invocation' -Action {
        Invoke-RestMethod -Method Post -Uri "$publisherBase/invoke-test" -TimeoutSec 5
    }
    if ($invocation.appId -ne 'cp6-p05-receiver' -or $invocation.message -ne 'p05-invocation') {
        throw 'Dapr service invocation returned an unexpected response.'
    }

    $publication = Invoke-WithRetry -Description 'Dapr Kafka publication' -Action {
        Invoke-RestMethod -Method Post -Uri "$publisherBase/publish-test" -TimeoutSec 5
    }
    $expectedTopic = 'cp6.platform.contract-example-changed.v1'
    $expectedKey = '11111111-1111-4111-8111-111111111111/example-1'
    if ($publication.topicName -ne $expectedTopic -or $publication.partitionKey -ne $expectedKey) {
        throw 'The publisher returned non-canonical Kafka addressing metadata.'
    }

    $delivery = Invoke-WithRetry -Description 'Dapr Kafka delivery' -Action {
        Invoke-RestMethod -Uri "$receiverBase/events/last" -TimeoutSec 3
    }
    if (-not $delivery.contractValid -or
        $delivery.eventId -ne 'evt-0001' -or
        $delivery.topicName -ne $expectedTopic -or
        $delivery.partitionKey -ne $expectedKey) {
        throw 'The receiver did not validate the expected Dapr/Kafka delivery.'
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        status = 'Passed'
        startedAtUtc = $startedAt.ToString('o')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        runtime = [ordered]@{
            dapr = 'daprio/daprd:1.18.2'
            kafka = 'apache/kafka:4.3.1'
        }
        serviceInvocation = [ordered]@{
            targetAppId = $invocation.appId
            message = $invocation.message
        }
        pubsub = [ordered]@{
            component = $publication.pubSubName
            topic = $delivery.topicName
            partitionKey = $delivery.partitionKey
            eventId = $delivery.eventId
            contractValid = $delivery.contractValid
        }
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'result.json') -Encoding utf8
    Write-Host "P05 real Dapr/Kafka integration passed for $($delivery.eventId)."
} finally {
    try {
        & docker compose --project-name $project --file $composeFile logs --no-color 2>&1 |
            Set-Content -LiteralPath (Join-Path $resolvedOutput 'docker-compose.log') -Encoding utf8
    } catch {
        Write-Warning "Could not preserve compose logs: $($_.Exception.Message)"
    }

    & docker compose --project-name $project --file $composeFile down --volumes --remove-orphans --rmi local 2>&1 | Out-Host
}
