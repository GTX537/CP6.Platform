[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0$')]
    [string]$PackageVersion,

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
    [ValidateSet('Success')]
    [string]$PreflightStatus,

    [Parameter(Mandatory)]
    [string]$TrustPolicyPath,

    [Parameter(Mandatory)]
    [string]$CertificateDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$ReleaseToolPath,

    [string]$DotNetPath = 'dotnet',

    [string]$CleanupProbePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:GITHUB_TOKEN)) {
    throw 'GITHUB_TOKEN is required for immutable formal publication.'
}
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$formalArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-formal'))
$formalPrefix = $formalArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedPackages = [IO.Path]::GetFullPath($PackagePath, $repositoryRoot)
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
if (-not $resolvedOutput.StartsWith($formalPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Formal publication output must stay below artifacts/p10-formal.'
}
if ((Test-Path -LiteralPath $resolvedOutput) -and @(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
    throw 'Formal publication output directory must be empty.'
}
foreach ($path in @($resolvedPackages, $resolvedCertificates)) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { throw "Required directory is missing: $path" }
}
if (-not (Test-Path -LiteralPath $resolvedPolicy -PathType Leaf)) { throw 'Formal trust policy is missing.' }

$packageIds = @(
    'CP6.Platform.Abstractions',
    'CP6.Platform.AspNetCore',
    'CP6.Platform.Contracts',
    'CP6.Platform.Deployment',
    'CP6.Platform.EntityFramework',
    'CP6.Platform.Messaging',
    'CP6.Platform.Release'
)
$packages = @(Get-ChildItem -LiteralPath $resolvedPackages -Filter '*.nupkg' -File | Sort-Object Name)
$expectedNames = @($packageIds | ForEach-Object { "$($_).$PackageVersion.nupkg" } | Sort-Object)
if (($packages.Name | ConvertTo-Json -Compress) -cne ($expectedNames | ConvertTo-Json -Compress)) {
    throw 'Formal publication requires the exact seven package files.'
}

$runnerTemp = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$resolvedRunnerTemp = [IO.Path]::GetFullPath($runnerTemp)
$runnerPrefix = $resolvedRunnerTemp.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$privateRoot = [IO.Path]::GetFullPath((Join-Path $resolvedRunnerTemp ('cp6-p10-publish-' + [Guid]::NewGuid().ToString('N'))))
if (-not $privateRoot.StartsWith($runnerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publication temporary path escaped RUNNER_TEMP.'
}
$downloadRoot = Join-Path $privateRoot 'downloads'
$rawRoot = Join-Path $privateRoot 'raw'
$preparedMarker = Join-Path $privateRoot 'p10-formal-version-consumed.json'
$preparedReadBack = Join-Path $privateRoot 'formal-package-readback.v1.json'
$markerPath = Join-Path $resolvedOutput 'p10-formal-version-consumed.json'
$publicDownloadRoot = Join-Path $resolvedOutput 'feed-readback-packages'
$publicReadBackPath = Join-Path $resolvedOutput 'formal-package-readback.v1.json'
$originalFeedUsername = $env:NUGET_FEED_USERNAME
$originalFeedToken = $env:NUGET_FEED_TOKEN
$markerWritten = $false
$failure = $null
$cleanupFailure = $null
$result = $null

function New-InjectedProcess([string]$Path, [string[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    if ($Path.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $startInfo.FileName = 'pwsh'
        $startInfo.ArgumentList.Add('-NoProfile')
        $startInfo.ArgumentList.Add('-File')
        $startInfo.ArgumentList.Add([IO.Path]::GetFullPath($Path))
    }
    else {
        $startInfo.FileName = $Path
    }
    foreach ($argument in $Arguments) { $startInfo.ArgumentList.Add($argument) }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    return $startInfo
}

function Invoke-Injected([string]$Path, [string[]]$Arguments) {
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = New-InjectedProcess $Path $Arguments
        if (-not $process.Start()) { throw "Unable to start $Path." }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        return [pscustomobject]@{
            ExitCode = $process.ExitCode
            Output = $standardOutput.GetAwaiter().GetResult()
            Error = $standardError.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-ReleaseTool([string[]]$Arguments) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath) -and $ReleaseToolPath.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $toolResult = Invoke-Injected $ReleaseToolPath $Arguments
        if ($toolResult.ExitCode -ne 0) { throw "Release tool failed for $($Arguments[0])." }
        return $toolResult.Output
    }
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        $toolResult = Invoke-Injected 'dotnet' (@([IO.Path]::GetFullPath($ReleaseToolPath)) + $Arguments)
    }
    else {
        $project = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        $toolResult = Invoke-Injected 'dotnet' (@('run', '--project', $project, '--configuration', 'Release', '--') + $Arguments)
    }
    if ($toolResult.ExitCode -ne 0) { throw "Release tool failed for $($Arguments[0])." }
    return $toolResult.Output
}

function Remove-PublicationTemporaryRoot {
    if (-not (Test-Path -LiteralPath $privateRoot)) { return }
    $resolved = [IO.Path]::GetFullPath($privateRoot)
    if (-not $resolved.StartsWith($runnerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to clean a path outside RUNNER_TEMP.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

try {
    [IO.Directory]::CreateDirectory($downloadRoot) | Out-Null
    [IO.Directory]::CreateDirectory($rawRoot) | Out-Null
    [IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null
    $null = Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)

    $marker = [ordered]@{
        marker = 'p10-formal-version-consumed'
        version = $PackageVersion
        sourceGitSha = $SourceGitSha
        runId = $RunId
        runAttempt = $RunAttempt
    }
    $rawMarker = Join-Path $rawRoot 'version-consumed.raw.json'
    [IO.File]::WriteAllText($rawMarker, ($marker | ConvertTo-Json -Compress), [Text.UTF8Encoding]::new($false))
    $null = Invoke-ReleaseTool @('canonicalize', $rawMarker, $preparedMarker)

    [IO.File]::Move($preparedMarker, $markerPath)
    $markerWritten = $true
    if ($env:GITHUB_ACTIONS -ceq 'true') { Write-Host "::add-mask::$($env:GITHUB_TOKEN)" }
    foreach ($packageId in $packageIds) {
        $packagePath = Join-Path $resolvedPackages "$packageId.$PackageVersion.nupkg"
        $push = Invoke-Injected $DotNetPath @(
            'nuget', 'push', $packagePath,
            '--source', 'https://nuget.pkg.github.com/GTX537/index.json',
            '--api-key', $env:GITHUB_TOKEN
        )
        if ($push.ExitCode -ne 0) { throw "Package upload failed for $packageId." }
    }

    $env:NUGET_FEED_USERNAME = 'GTX537'
    $env:NUGET_FEED_TOKEN = $env:GITHUB_TOKEN
    $evaluationUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    $readBackPackages = @()
    foreach ($packageId in $packageIds) {
        $localPath = Join-Path $resolvedPackages "$packageId.$PackageVersion.nupkg"
        $downloadPath = Join-Path $downloadRoot "$packageId.$PackageVersion.nupkg"
        $null = Invoke-ReleaseTool @(
            'download-package',
            'https://nuget.pkg.github.com/GTX537/index.json',
            $packageId,
            $PackageVersion,
            $downloadPath)
        $localHash = (Get-FileHash -LiteralPath $localPath -Algorithm SHA256).Hash.ToLowerInvariant()
        $downloadHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($localHash -cne $downloadHash) { throw "Read-back hash mismatch for $packageId." }
        $verificationOutput = Invoke-ReleaseTool @(
            'verify-formal-package',
            $downloadPath,
            $resolvedPolicy,
            $resolvedCertificates,
            $packageId,
            $PackageVersion,
            $SourceGitSha,
            $evaluationUtc,
            'Current')
        $jsonLine = @(($verificationOutput -split "`r?`n") | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1)
        if ($jsonLine.Count -ne 1) { throw "Formal verifier returned no identity for $packageId." }
        $verified = $jsonLine[0] | ConvertFrom-Json -Depth 20
        if ([string]$verified.packageId -cne $packageId -or
            [string]$verified.version -cne $PackageVersion -or
            [string]$verified.sourceGitSha -cne $SourceGitSha -or
            [string]$verified.packageSha256 -cne $downloadHash) {
            throw "Formal verifier identity does not match read-back bytes for $packageId."
        }
        $readBackPackages += [ordered]@{
            packageId = $packageId
            version = $PackageVersion
            sourceGitSha = $SourceGitSha
            authorSignedPackageSha256 = $localHash
            publishedPackageSha256 = $downloadHash
            feedIdentity = "https://nuget.pkg.github.com/GTX537/index.json#$packageId/$PackageVersion"
            signerFingerprint = [string]$verified.signerFingerprint
            spkiKeyId = [string]$verified.spkiKeyId
            timestampPolicyOid = [string]$verified.timestampPolicyOid
            timestampUtc = [string]$verified.timestampUtc
            timestampCertificateChainSha256 = @($verified.timestampCertificateChainSha256)
        }
    }

    $readBack = [ordered]@{
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
        version = $PackageVersion
        sourceGitSha = $SourceGitSha
        packages = $readBackPackages
    }
    $rawReadBack = Join-Path $rawRoot 'formal-package-readback.raw.json'
    [IO.File]::WriteAllText($rawReadBack, ($readBack | ConvertTo-Json -Depth 30 -Compress), [Text.UTF8Encoding]::new($false))
    $null = Invoke-ReleaseTool @('canonicalize', $rawReadBack, $preparedReadBack)
    [IO.Directory]::CreateDirectory($publicDownloadRoot) | Out-Null
    foreach ($downloadedPackage in @(Get-ChildItem -LiteralPath $downloadRoot -Filter '*.nupkg' -File)) {
        [IO.File]::Copy($downloadedPackage.FullName, (Join-Path $publicDownloadRoot $downloadedPackage.Name))
    }
    [IO.File]::Copy($preparedReadBack, $publicReadBackPath)
    $result = [pscustomobject]@{
        Status = 'Success'
        MarkerPath = $markerPath
        ReadBackPath = $publicReadBackPath
        PackagePath = $publicDownloadRoot
    }
}
catch {
    $failure = $_.Exception
}
finally {
    try {
        Remove-PublicationTemporaryRoot
        if (-not [string]::IsNullOrWhiteSpace($CleanupProbePath)) {
            $cleanup = Invoke-Injected $CleanupProbePath @()
            if ($cleanup.ExitCode -ne 0) { throw 'Injected publication cleanup proof failed.' }
        }
    }
    catch {
        $cleanupFailure = $_.Exception
    }
    $env:NUGET_FEED_USERNAME = $originalFeedUsername
    $env:NUGET_FEED_TOKEN = $originalFeedToken
}

if ($failure -or $cleanupFailure) {
    $message = if ($markerWritten) {
        'p10-formal-version-consumed: publication or read-back failed after the immutable version marker.'
    }
    else {
        'p10-formal-pre-upload-failure: publication stopped before the immutable version marker.'
    }
    $inner = if ($failure) { $failure } else { $cleanupFailure }
    throw [InvalidOperationException]::new("$message Cause: $($inner.Message)", $inner)
}

$result
