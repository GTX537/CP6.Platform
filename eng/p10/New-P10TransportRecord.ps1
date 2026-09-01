[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$SourceGitSha,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$RunId,

    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$RunAttempt,

    [Parameter(Mandatory)]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$WorkflowPath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$WorkflowFileSha,

    [Parameter(Mandatory)]
    [string]$Environment,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$PackageArtifactId,

    [Parameter(Mandatory)]
    [ValidatePattern('^sha256:[0-9a-f]{64}$')]
    [string]$PackageArtifactDigest,

    [Parameter(Mandatory)]
    [string]$ArtifactCreatedAtUtc,

    [Parameter(Mandatory)]
    [string]$ArtifactExpiresAtUtc,

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
    $ArtifactCreatedAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
$expires = [DateTimeOffset]::Parse(
    $ArtifactExpiresAtUtc,
    [Globalization.CultureInfo]::InvariantCulture,
    [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
if (-not $ArtifactCreatedAtUtc.EndsWith('Z', [StringComparison]::Ordinal) -or
    -not $ArtifactExpiresAtUtc.EndsWith('Z', [StringComparison]::Ordinal) -or
    $expires -le $created) {
    throw 'Artifact API timestamps must be UTC and expiry must follow creation.'
}

function Format-UtcMilliseconds([DateTimeOffset]$Value) {
    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
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
    platformSourceSha = $SourceGitSha
    testOnly = $true
    workflow = [ordered]@{
        commitSha = $SourceGitSha
        environment = $Environment
        repository = $Repository
        runAttempt = $RunAttempt
        runId = $RunId
        workflowFileSha = $WorkflowFileSha
        workflowPath = $WorkflowPath
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
