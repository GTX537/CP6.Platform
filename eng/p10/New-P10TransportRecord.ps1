[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$PlatformSourceSha,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$RunId,

    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$RunAttempt,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$PackageArtifactId,

    [Parameter(Mandatory)]
    [ValidatePattern('^sha256:[0-9a-f]{64}$')]
    [string]$PackageArtifactDigest,

    [Parameter(Mandatory)]
    [string]$CreatedAtUtc,

    [Parameter(Mandatory)]
    [string]$ExpiresAtUtc,

    [string]$OutputPath = 'artifacts/p10-test/transport/test-package-transport.v1.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Transport output must be below $artifactsRoot."
}

$created = [DateTimeOffset]::Parse(
    $CreatedAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
$expires = [DateTimeOffset]::Parse(
    $ExpiresAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
if (-not $CreatedAtUtc.EndsWith('Z', [StringComparison]::Ordinal) -or
    -not $ExpiresAtUtc.EndsWith('Z', [StringComparison]::Ordinal) -or
    $expires -le $created) {
    throw 'Artifact API timestamps must be UTC and expiry must follow creation.'
}

function Format-UtcMilliseconds([DateTimeOffset]$Value) {
    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
}

$workflowPath = '.github/workflows/p10-test-candidate.yml'
$workflowFullPath = Join-Path $repositoryRoot $workflowPath
if (-not (Test-Path -LiteralPath $workflowFullPath -PathType Leaf)) {
    throw 'P10 workflow file is missing.'
}
$workflowFileSha = (& git -C $repositoryRoot hash-object $workflowFullPath).Trim()
if ($LASTEXITCODE -ne 0 -or $workflowFileSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'Could not calculate the exact workflow file Git blob SHA.'
}

$record = [ordered]@{
    '$schemaId' = 'https://schemas.cp6.dev/release/test-package-transport.v1'
    createdAtUtc = Format-UtcMilliseconds $created
    expiresAtUtc = Format-UtcMilliseconds $expires
    packageArtifact = [ordered]@{
        artifactId = $PackageArtifactId
        digest = $PackageArtifactDigest
        sourceRunAttempt = $RunAttempt
        sourceRunId = $RunId
    }
    platformSourceSha = $PlatformSourceSha
    testOnly = $true
    workflow = [ordered]@{
        commitSha = $PlatformSourceSha
        environment = 'test'
        repository = 'GTX537/CP6.Platform'
        runAttempt = $RunAttempt
        runId = $RunId
        workflowFileSha = $workflowFileSha
        workflowPath = $workflowPath
    }
}

$outputDirectory = Split-Path -Parent $resolvedOutput
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$privateRoot = Join-Path $artifactsRoot ("private\transport-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($privateRoot) | Out-Null
$rawPath = Join-Path $privateRoot 'test-package-transport.raw.json'
$toolPath = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\bin\Release\net8.0\CP6.Platform.ReleaseTool.dll'
try {
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw 'Release tool build output is missing.'
    }
    $json = $record | ConvertTo-Json -Depth 20 -Compress
    [IO.File]::WriteAllText($rawPath, $json, [Text.UTF8Encoding]::new($false))
    & dotnet $toolPath canonicalize $rawPath $resolvedOutput | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Transport canonicalization failed.' }
    $evaluationUtc = [DateTimeOffset]::UtcNow.UtcDateTime.ToString('O', [Globalization.CultureInfo]::InvariantCulture)
    & dotnet $toolPath validate-transport $resolvedOutput $evaluationUtc | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Transport contract validation failed.' }
}
finally {
    if (Test-Path -LiteralPath $privateRoot) {
        Remove-Item -LiteralPath $privateRoot -Recurse -Force
    }
}

Write-Host "Created test package transport record for artifact $PackageArtifactId."
