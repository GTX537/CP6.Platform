[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ReadBackPath,

    [Parameter(Mandatory)]
    [string]$WindowsVerificationPath,

    [Parameter(Mandatory)]
    [string]$LinuxVerificationEvidencePath,

    [Parameter(Mandatory)]
    [ValidateSet('Success')]
    [string]$LinuxVerification,

    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$SourceGitSha,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$WorkflowFileSha,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$RunId,

    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$RunAttempt,

    [Parameter(Mandatory)]
    [string]$DotNetSdk,

    [Parameter(Mandatory)]
    [string]$NuGetClient,

    [Parameter(Mandatory)]
    [string]$RunnerImage,

    [Parameter(Mandatory)]
    [string]$TrustPolicyPath,

    [Parameter(Mandatory)]
    [string]$CertificateDirectory,

    [Parameter(Mandatory)]
    [string]$OutputPath,

    [string]$ReleaseToolPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resolvedReadBack = [IO.Path]::GetFullPath($ReadBackPath, $repositoryRoot)
$resolvedWindows = [IO.Path]::GetFullPath($WindowsVerificationPath, $repositoryRoot)
$resolvedLinux = [IO.Path]::GetFullPath($LinuxVerificationEvidencePath, $repositoryRoot)
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
foreach ($path in @($resolvedReadBack, $resolvedWindows, $resolvedLinux, $resolvedPolicy)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required publication evidence is missing: $path" }
}
if (-not (Test-Path -LiteralPath $resolvedCertificates -PathType Container)) { throw 'Formal certificate directory is missing.' }
if (Test-Path -LiteralPath $resolvedOutput) { throw 'Final formal publication record already exists.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($resolvedOutput)) | Out-Null

function Invoke-ReleaseTool([string[]]$Arguments) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        & dotnet ([IO.Path]::GetFullPath($ReleaseToolPath)) @Arguments | Out-Host
    }
    else {
        $project = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        & dotnet run --project $project --configuration Release -- @Arguments | Out-Host
    }
    if ($LASTEXITCODE -ne 0) { throw "Release tool failed for $($Arguments[0])." }
}

function Get-TimestampLeafHash([object[]]$Chain, [string]$EvidenceName, [string]$PackageId) {
    $hashes = @($Chain | ForEach-Object { [string]$_ })
    if ($hashes.Count -eq 0 -or
        @($hashes | Where-Object { $_ -cnotmatch '^[0-9a-f]{64}$' }).Count -ne 0 -or
        @($hashes | Select-Object -Unique).Count -ne $hashes.Count) {
        throw "$EvidenceName timestamp certificate chain is malformed for $PackageId."
    }

    return $hashes[0]
}

Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)
$readBack = Get-Content -LiteralPath $resolvedReadBack -Raw | ConvertFrom-Json -Depth 30
$windows = Get-Content -LiteralPath $resolvedWindows -Raw | ConvertFrom-Json -Depth 30
$linux = Get-Content -LiteralPath $resolvedLinux -Raw | ConvertFrom-Json -Depth 30
if ($readBack.version -cne $PackageVersion -or $readBack.sourceGitSha -cne $SourceGitSha -or
    $windows.version -cne $PackageVersion -or $windows.sourceGitSha -cne $SourceGitSha -or
    $linux.version -cne $PackageVersion -or $linux.sourceGitSha -cne $SourceGitSha -or
    $windows.mode -cne 'Current' -or $linux.mode -cne 'Current') {
    throw 'Publication evidence does not bind one version and source SHA.'
}

$packageIds = @(
    'CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Contracts',
    'CP6.Platform.Deployment', 'CP6.Platform.EntityFramework', 'CP6.Platform.Messaging', 'CP6.Platform.Release'
)
foreach ($evidence in @($readBack, $windows, $linux)) {
    $ids = @($evidence.packages | ForEach-Object { [string]$_.packageId })
    if (($ids | ConvertTo-Json -Compress) -cne ($packageIds | ConvertTo-Json -Compress)) {
        throw 'Publication verification evidence must contain the exact ordinal package set.'
    }
}

$policy = Get-Content -LiteralPath $resolvedPolicy -Raw | ConvertFrom-Json -Depth 20
$currentSigner = @($policy.signers | Where-Object { $_.status -ceq 'Current' })
if ($currentSigner.Count -ne 1) { throw 'Formal trust policy must have exactly one Current signer.' }
$policyHash = (Get-FileHash -LiteralPath $resolvedPolicy -Algorithm SHA256).Hash.ToLowerInvariant()
$packages = @()
foreach ($packageId in $packageIds) {
    $readBackPackage = @($readBack.packages | Where-Object { $_.packageId -ceq $packageId })[0]
    $windowsPackage = @($windows.packages | Where-Object { $_.packageId -ceq $packageId })[0]
    $linuxPackage = @($linux.packages | Where-Object { $_.packageId -ceq $packageId })[0]
    $publishedHash = [string]$readBackPackage.publishedPackageSha256
    $readBackChain = @($readBackPackage.timestampCertificateChainSha256)
    $windowsChain = @($windowsPackage.timestampCertificateChainSha256)
    $linuxChain = @($linuxPackage.timestampCertificateChainSha256)
    $readBackTimestampLeaf = Get-TimestampLeafHash $readBackChain 'Feed read-back' $packageId
    $windowsTimestampLeaf = Get-TimestampLeafHash $windowsChain 'Windows' $packageId
    $linuxTimestampLeaf = Get-TimestampLeafHash $linuxChain 'Linux' $packageId
    if ($publishedHash -cne [string]$readBackPackage.authorSignedPackageSha256 -or
        $publishedHash -cne [string]$windowsPackage.packageSha256 -or
        $publishedHash -cne [string]$linuxPackage.packageSha256 -or
        [string]$windowsPackage.packageId -cne $packageId -or
        [string]$linuxPackage.packageId -cne $packageId -or
        [string]$windowsPackage.version -cne $PackageVersion -or
        [string]$linuxPackage.version -cne $PackageVersion -or
        [string]$windowsPackage.sourceGitSha -cne $SourceGitSha -or
        [string]$linuxPackage.sourceGitSha -cne $SourceGitSha -or
        [string]$readBackPackage.signerFingerprint -cne [string]$currentSigner[0].certificateSha256 -or
        [string]$windowsPackage.signerFingerprint -cne [string]$currentSigner[0].certificateSha256 -or
        [string]$linuxPackage.signerFingerprint -cne [string]$currentSigner[0].certificateSha256 -or
        [string]$readBackPackage.spkiKeyId -cne [string]$currentSigner[0].spkiKeyId -or
        [string]$windowsPackage.spkiKeyId -cne [string]$currentSigner[0].spkiKeyId -or
        [string]$linuxPackage.spkiKeyId -cne [string]$currentSigner[0].spkiKeyId -or
        [string]$windowsPackage.timestampPolicyOid -cne [string]$readBackPackage.timestampPolicyOid -or
        [string]$linuxPackage.timestampPolicyOid -cne [string]$readBackPackage.timestampPolicyOid -or
        $readBackTimestampLeaf -cne $windowsTimestampLeaf -or
        $readBackTimestampLeaf -cne $linuxTimestampLeaf) {
        throw "Windows, Linux, feed, and trust evidence differ for $packageId."
    }
    $packages += [ordered]@{
        packageId = $packageId
        version = $PackageVersion
        sourceGitSha = $SourceGitSha
        authorSignedPackageSha256 = $publishedHash
        publishedPackageSha256 = $publishedHash
        feedIdentity = [string]$readBackPackage.feedIdentity
        feedTransformation = 'BytePreserving'
        signerFingerprint = [string]$currentSigner[0].certificateSha256
        timestampPolicy = 'Rfc3161Required'
        timestampPolicyOid = [string]$readBackPackage.timestampPolicyOid
        timestampCertificateChainSha256 = @($readBackPackage.timestampCertificateChainSha256)
    }
}

$record = [ordered]@{
    '$schemaId' = 'https://schemas.cp6.dev/release/formal-package-publication.v1'
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
    version = $PackageVersion
    sourceGitSha = $SourceGitSha
    buildInvocationId = "p10-s04:$SourceGitSha`:$RunId`:$RunAttempt"
    workflow = [ordered]@{
        repository = 'GTX537/CP6.Platform'
        workflowPath = '.github/workflows/p10-formal-packages.yml'
        workflowFileSha = $WorkflowFileSha
        commitSha = $SourceGitSha
        runId = $RunId
        runAttempt = $RunAttempt
        environment = 'p10-formal-release'
    }
    toolchain = [ordered]@{
        dotnetSdk = $DotNetSdk
        nugetClient = $NuGetClient
        runnerImage = $RunnerImage
    }
    trust = [ordered]@{
        policyVersion = [int]$policy.policyVersion
        policySha256 = $policyHash
        trustModel = 'PinnedSelfSigned'
        publicCaTrusted = $false
        internallyTrusted = $true
        signerFingerprint = [string]$currentSigner[0].certificateSha256
        spkiKeyId = [string]$currentSigner[0].spkiKeyId
        timestampPolicy = 'Rfc3161Required'
        timestampService = 'http://timestamp.digicert.com'
    }
    packages = $packages
    verification = [ordered]@{
        windows = 'Success'
        linux = $LinuxVerification
    }
}

$rawPath = "$resolvedOutput.raw"
[IO.File]::WriteAllText($rawPath, ($record | ConvertTo-Json -Depth 40 -Compress), [Text.UTF8Encoding]::new($false))
try {
    Invoke-ReleaseTool @('canonicalize', $rawPath, $resolvedOutput)
    Invoke-ReleaseTool @(
        'validate-formal-publication',
        $resolvedOutput,
        $resolvedPolicy,
        $resolvedCertificates,
        [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture))
}
catch {
    if (Test-Path -LiteralPath $resolvedOutput) { Remove-Item -LiteralPath $resolvedOutput -Force }
    throw
}
finally {
    if (Test-Path -LiteralPath $rawPath) { Remove-Item -LiteralPath $rawPath -Force }
}

[pscustomobject]@{
    Status = 'Success'
    OutputPath = $resolvedOutput
    Sha256 = (Get-FileHash -LiteralPath $resolvedOutput -Algorithm SHA256).Hash.ToLowerInvariant()
}
