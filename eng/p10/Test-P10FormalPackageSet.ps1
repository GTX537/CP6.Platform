[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedSourceGitSha,

    [Parameter(Mandatory)]
    [string]$TrustPolicyPath,

    [Parameter(Mandatory)]
    [string]$CertificateDirectory,

    [Parameter(Mandatory)]
    [string]$BuildProvenancePath,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$ReleaseToolPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$formalArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-formal'))
$formalPrefix = $formalArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPackages = [IO.Path]::GetFullPath($PackagePath, $repositoryRoot)
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)
$resolvedProvenance = [IO.Path]::GetFullPath($BuildProvenancePath, $repositoryRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
if (-not $resolvedOutput.StartsWith($formalPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Formal verification output must stay below artifacts/p10-formal.'
}
foreach ($requiredPath in @($resolvedPackages, $resolvedCertificates)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Container)) {
        throw "Required directory is missing: $requiredPath"
    }
}
foreach ($requiredPath in @($resolvedPolicy, $resolvedProvenance)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Required file is missing: $requiredPath"
    }
}
if ((Test-Path -LiteralPath $resolvedOutput) -and @(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
    throw 'Formal verification output directory must be empty.'
}
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$packageIds = @(
    'CP6.Platform.Abstractions',
    'CP6.Platform.AspNetCore',
    'CP6.Platform.Contracts',
    'CP6.Platform.Deployment',
    'CP6.Platform.EntityFramework',
    'CP6.Platform.Messaging',
    'CP6.Platform.Release'
)

function Invoke-ReleaseTool([string[]]$Arguments, [switch]$Capture) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        $resolvedTool = [IO.Path]::GetFullPath($ReleaseToolPath)
        $command = @($resolvedTool) + $Arguments
    }
    else {
        $project = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        $command = @('run', '--project', $project, '--configuration', 'Release', '--') + $Arguments
    }
    $output = @(& dotnet @command 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Release tool failed for $($Arguments[0]) with exit code $LASTEXITCODE."
    }
    if ($Capture) { return $output }
    $output | Out-Host
}

Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)
Invoke-ReleaseTool @('validate-build-provenance', $resolvedProvenance)
$provenance = Get-Content -LiteralPath $resolvedProvenance -Raw | ConvertFrom-Json -Depth 20
$finalById = @{}
foreach ($item in $provenance.finalPackages) {
    $finalById[[string]$item.packageId] = [string]$item.finalSha256
}

$actualPackages = @(Get-ChildItem -LiteralPath $resolvedPackages -Filter '*.nupkg' -File | Sort-Object Name)
$expectedNames = @($packageIds | ForEach-Object { "$($_).$ExpectedVersion.nupkg" } | Sort-Object)
if (($actualPackages.Name | ConvertTo-Json -Compress) -cne ($expectedNames | ConvertTo-Json -Compress)) {
    throw "Formal verification package set is not exact: $($actualPackages.Name -join ', ')."
}

$evaluationUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
$results = @()
foreach ($packageId in $packageIds) {
    $package = Get-Item -LiteralPath (Join-Path $resolvedPackages "$packageId.$ExpectedVersion.nupkg")
    $output = @(Invoke-ReleaseTool @(
        'verify-formal-package',
        $package.FullName,
        $resolvedPolicy,
        $resolvedCertificates,
        $packageId,
        $ExpectedVersion,
        $ExpectedSourceGitSha,
        $evaluationUtc,
        'Current'
    ) -Capture)
    $jsonLine = @($output | ForEach-Object { [string]$_ } | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1)
    if ($jsonLine.Count -ne 1) {
        throw "Formal verifier did not return one JSON result for $packageId."
    }
    $result = $jsonLine[0] | ConvertFrom-Json -Depth 20
    $actualHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualHash -cne [string]$result.packageSha256 -or
        -not $finalById.ContainsKey($packageId) -or
        $actualHash -cne [string]$finalById[$packageId]) {
        throw "Formal package hash does not bind verification and provenance for $packageId."
    }
    $results += $result
}

$rawPath = Join-Path $resolvedOutput 'formal-package-verification.raw.json'
$resultPath = Join-Path $resolvedOutput 'formal-package-verification.v1.json'
$record = [ordered]@{
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    version = $ExpectedVersion
    sourceGitSha = $ExpectedSourceGitSha
    mode = 'Current'
    packages = $results
}
[IO.File]::WriteAllText($rawPath, ($record | ConvertTo-Json -Depth 30 -Compress), [Text.UTF8Encoding]::new($false))
try {
    Invoke-ReleaseTool @('canonicalize', $rawPath, $resultPath)
}
finally {
    if (Test-Path -LiteralPath $rawPath) { Remove-Item -LiteralPath $rawPath -Force }
}

[pscustomobject]@{
    PackageCount = $results.Count
    ResultPath = $resultPath
}
