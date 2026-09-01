[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$outputRoot = Join-Path $repositoryRoot 'artifacts/p06-sql-integration'
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = [IO.Path]::GetFullPath($outputRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$containerName = "cp6-p06-sql-$PID"
$sqlPassword = "CP6_P06!Sql_${PID}_Strong"
$sqlImage = 'mcr.microsoft.com/mssql/server@sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89'
$containerStarted = $false
$dotnetCommand = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
    $env:DOTNET_HOST_PATH
}
else {
    (Get-Command -Name 'dotnet' -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}

if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write P06 evidence outside $artifactsRoot."
}

if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

& docker version --format '{{.Server.Version}}' | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw 'A running Docker engine is required for the P06 real SQL Server profile.'
}

$startedAt = [DateTimeOffset]::UtcNow
try {
    $containerId = (& docker run --detach --name $containerName --env ACCEPT_EULA=Y --env MSSQL_SA_PASSWORD=$sqlPassword --publish '127.0.0.1::1433' $sqlImage).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw 'Could not start the pinned P06 SQL Server container.'
    }
    $containerStarted = $true

    $binding = (& docker port $containerName '1433/tcp').Trim()
    if ($LASTEXITCODE -ne 0 -or $binding -notmatch ':(?<port>[0-9]+)$') {
        throw "Could not resolve the SQL Server port from '$binding'."
    }
    $sqlPort = [int]$Matches.port

    $ready = $false
    for ($attempt = 1; $attempt -le 60; $attempt++) {
        $logs = (& docker logs $containerName 2>&1 | Out-String)
        if ($logs.Contains('SQL Server is now ready for client connections', [StringComparison]::Ordinal)) {
            $ready = $true
            break
        }

        Start-Sleep -Seconds 1
    }
    if (-not $ready) {
        throw 'SQL Server did not become ready within 60 seconds.'
    }

    $env:CP6_P06_SQL_CONNECTION = "Server=127.0.0.1,$sqlPort;User ID=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=True"
    & $dotnetCommand run --project (Join-Path $repositoryRoot 'tests/CP6.Platform.SqlServerFixture/CP6.Platform.SqlServerFixture.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) {
        throw "P06 SQL Server fixture failed with exit code $LASTEXITCODE."
    }

    $evidence = [ordered]@{
        schemaVersion = 1
        status = 'Passed'
        startedAtUtc = $startedAt.ToString('o')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        runtime = [ordered]@{
            sqlServer = '2022-CU26-ubuntu-22.04'
            imageDigest = 'sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89'
        }
        checks = @(
            'business-outbox-commit-and-rollback',
            'conditional-lease-race',
            'ack-then-crash-redelivery',
            'inbox-duplicate-and-payload-hash-conflict',
            'aggregate-version-ordering',
            'poison-rollback-dlq-and-authorized-replay',
            'retention-7-30-90-days'
        )
    }
    $evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'result.json') -Encoding utf8
} finally {
    Remove-Item Env:CP6_P06_SQL_CONNECTION -ErrorAction SilentlyContinue
    if ($containerStarted) {
        try {
            & docker logs $containerId 2>&1 | Set-Content -LiteralPath (Join-Path $resolvedOutput 'sql-server.log') -Encoding utf8
        } catch {
            Write-Warning "Could not preserve SQL Server logs: $($_.Exception.Message)"
        }

        & docker rm --force $containerId 2>&1 | Out-Host
    }
}
