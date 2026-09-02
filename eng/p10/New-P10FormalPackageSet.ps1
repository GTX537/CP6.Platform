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
    [ValidatePattern('^0\.10\.0$')]
    [string]$PackageVersion,

    [string]$OutputPath = 'artifacts/p10-formal/package-set',

    [string]$TrustPolicyPath = 'eng/p10/trust/p10-formal-nuget-trust-store.v1.json',

    [string]$CertificateDirectory = 'eng/p10/trust/certificates',

    [switch]$InjectFailureAfterSigning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'Formal package signing must run on the protected Windows runner.'
}
if ([string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_PASSWORD)) {
    throw 'Both formal signing Environment Secrets are required.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$formalArtifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-formal'))
$formalPrefix = $formalArtifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
if (-not $resolvedOutput.StartsWith($formalPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Formal package-set output must stay below artifacts/p10-formal.'
}
if ((Test-Path -LiteralPath $resolvedOutput) -and @(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
    throw 'Formal package-set output directory must be empty.'
}
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)
if (-not (Test-Path -LiteralPath $resolvedPolicy -PathType Leaf) -or
    -not (Test-Path -LiteralPath $resolvedCertificates -PathType Container)) {
    throw 'Committed formal trust assets are missing.'
}

Push-Location $repositoryRoot
try {
    $checkoutSha = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $checkoutSha -cne $SourceGitSha) {
        throw 'SourceGitSha must equal the checked-out commit.'
    }
}
finally {
    Pop-Location
}

$runnerTemp = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) { [IO.Path]::GetTempPath() } else { $env:RUNNER_TEMP }
$resolvedRunnerTemp = [IO.Path]::GetFullPath($runnerTemp)
$runnerPrefix = $resolvedRunnerTemp.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$randomBytes = [byte[]]::new(16)
[Security.Cryptography.RandomNumberGenerator]::Fill($randomBytes)
$randomName = [Convert]::ToHexString($randomBytes).ToLowerInvariant()
[Array]::Clear($randomBytes, 0, $randomBytes.Length)
$privateRoot = [IO.Path]::GetFullPath((Join-Path $resolvedRunnerTemp "cp6-p10-formal-$randomName"))
if (-not $privateRoot.StartsWith($runnerPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Formal private directory escaped RUNNER_TEMP.'
}
$buildRoot = Join-Path $privateRoot 'build'
$rawRoot = Join-Path $privateRoot 'raw'
$pfxPath = Join-Path $privateRoot 'p10-formal-signing-private.pfx'
$runtimeRoot = Join-Path $resolvedOutput 'packages'
$symbolRoot = Join-Path $resolvedOutput 'symbols'
$evidenceRoot = Join-Path $resolvedOutput 'evidence'
$verificationRoot = Join-Path $resolvedOutput 'verification\windows'
$provenancePath = Join-Path $evidenceRoot 'build-invocation-provenance.v1.json'
$pfxBytes = $null
$certificate = $null
$publicKey = $null
$derBytes = $null
$spkiBytes = $null
$password = $env:P10_NUGET_SIGNING_PFX_PASSWORD
$completed = $false

function Invoke-CheckedDotNet([string[]]$Arguments) {
    & dotnet @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ReleaseTool([string[]]$Arguments) {
    $toolPath = Join-Path $buildRoot 'bin\CP6.Platform.ReleaseTool\release\CP6.Platform.ReleaseTool.dll'
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw 'The one solution build did not produce the Release tool.'
    }
    Invoke-CheckedDotNet (@($toolPath) + $Arguments)
}

function Format-UtcMilliseconds([DateTimeOffset]$Value) {
    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
}

function Remove-FormalDirectory([string]$Path, [string]$RequiredPrefix) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($RequiredPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a formal directory outside its approved root.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

try {
    [IO.Directory]::CreateDirectory($buildRoot) | Out-Null
    [IO.Directory]::CreateDirectory($rawRoot) | Out-Null
    [IO.Directory]::CreateDirectory($runtimeRoot) | Out-Null
    [IO.Directory]::CreateDirectory($symbolRoot) | Out-Null
    [IO.Directory]::CreateDirectory($evidenceRoot) | Out-Null
    [IO.Directory]::CreateDirectory($verificationRoot) | Out-Null

    Push-Location $repositoryRoot
    try {
        & dotnet restore CP6.Platform.sln "-p:ArtifactsPath=$buildRoot" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Formal solution restore failed.' }
        & dotnet build CP6.Platform.sln `
            --configuration Release `
            --no-restore `
            "-p:ArtifactsPath=$buildRoot" `
            -p:ContinuousIntegrationBuild=true `
            "-p:RepositoryCommit=$SourceGitSha" | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Formal one-build solution build failed.' }
    }
    finally {
        Pop-Location
    }

    Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)
    $policy = Get-Content -LiteralPath $resolvedPolicy -Raw | ConvertFrom-Json -Depth 20
    $currentSigners = @($policy.signers | Where-Object { $_.status -ceq 'Current' })
    if ($currentSigners.Count -ne 1) { throw 'Formal trust policy must select exactly one Current signer.' }
    $currentSigner = $currentSigners[0]

    try {
        $pfxBytes = [Convert]::FromBase64String($env:P10_NUGET_SIGNING_PFX_BASE64)
    }
    catch {
        throw 'Formal PFX Secret is not valid base64.'
    }
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $pfxBytes,
        $password,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    if (-not $certificate.HasPrivateKey) { throw 'Formal PFX does not contain a private key.' }
    $derBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $certificateSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($derBytes)).ToLowerInvariant()
    $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    $spkiBytes = $publicKey.ExportSubjectPublicKeyInfo()
    $spkiKeyId = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($spkiBytes)).ToLowerInvariant()
    $committedCertificate = Join-Path $resolvedCertificates "$certificateSha256.cer"
    if ($certificateSha256 -cne [string]$currentSigner.certificateSha256 -or
        $spkiKeyId -cne [string]$currentSigner.spkiKeyId -or
        -not (Test-Path -LiteralPath $committedCertificate -PathType Leaf) -or
        (Get-FileHash -LiteralPath $committedCertificate -Algorithm SHA256).Hash.ToLowerInvariant() -cne $certificateSha256) {
        throw 'Environment PFX does not match the committed Current signer.'
    }

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Pack-P10FormalPackages.ps1') `
        -PackageVersion $PackageVersion `
        -SourceGitSha $SourceGitSha `
        -BuildArtifactsPath $buildRoot `
        -OutputPath $runtimeRoot `
        -SymbolOutputPath $symbolRoot | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Formal package packing failed.' }

    $packageIds = @(
        'CP6.Platform.Abstractions',
        'CP6.Platform.AspNetCore',
        'CP6.Platform.Contracts',
        'CP6.Platform.Deployment',
        'CP6.Platform.EntityFramework',
        'CP6.Platform.Messaging',
        'CP6.Platform.Release'
    )
    $preSign = @($packageIds | ForEach-Object {
        $package = Get-Item -LiteralPath (Join-Path $runtimeRoot "$($_).$PackageVersion.nupkg")
        [ordered]@{
            packageId = $_
            sha256 = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    })

    [IO.File]::WriteAllBytes($pfxPath, $pfxBytes)
    if ($env:GITHUB_ACTIONS -ceq 'true') {
        Write-Host "::add-mask::$password"
    }
    foreach ($packageId in $packageIds) {
        $package = Join-Path $runtimeRoot "$packageId.$PackageVersion.nupkg"
        & dotnet nuget sign $package `
            --certificate-path $pfxPath `
            --certificate-password $password `
            --hash-algorithm SHA256 `
            --timestamper 'http://timestamp.digicert.com' `
            --timestamp-hash-algorithm SHA256 `
            --overwrite | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Formal signing failed for $packageId." }
    }
    $finalPackages = @($packageIds | ForEach-Object {
        $packageId = $_
        $preSignHash = [string]($preSign | Where-Object { $_.packageId -ceq $packageId }).sha256
        $finalHash = (Get-FileHash -LiteralPath (Join-Path $runtimeRoot "$packageId.$PackageVersion.nupkg") -Algorithm SHA256).Hash.ToLowerInvariant()
        [ordered]@{
            packageId = $packageId
            preSignSha256 = $preSignHash
            finalSha256 = $finalHash
            subject = [ordered]@{
                subjectKind = 'Package'
                subjectName = $packageId
                sha256OrDigest = $finalHash
                sourceGitSha = $SourceGitSha
            }
        }
    })
    $runnerIdentity = if ([string]::IsNullOrWhiteSpace($env:ImageOS)) {
        [Runtime.InteropServices.RuntimeInformation]::OSDescription
    }
    else {
        $env:ImageOS
    }
    $provenance = [ordered]@{
        '$schemaId' = 'https://schemas.cp6.dev/release/build-invocation-provenance.v1'
        createdAtUtc = Format-UtcMilliseconds ([DateTimeOffset]::UtcNow)
        sourceGitSha = $SourceGitSha
        buildInvocationId = "p10-s04:$SourceGitSha`:$RunId`:$RunAttempt"
        toolchain = [ordered]@{
            dotnetSdk = (& dotnet --version).Trim()
            runner = $runnerIdentity
        }
        preSignOutputs = $preSign
        finalPackages = $finalPackages
    }
    $rawProvenance = Join-Path $rawRoot 'build-invocation-provenance.raw.json'
    [IO.File]::WriteAllText($rawProvenance, ($provenance | ConvertTo-Json -Depth 30 -Compress), [Text.UTF8Encoding]::new($false))
    Invoke-ReleaseTool @('canonicalize', $rawProvenance, $provenancePath)
    Invoke-ReleaseTool @('validate-build-provenance', $provenancePath)

    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-P10FormalPackageSet.ps1') `
        -PackagePath $runtimeRoot `
        -ExpectedVersion $PackageVersion `
        -ExpectedSourceGitSha $SourceGitSha `
        -TrustPolicyPath $resolvedPolicy `
        -CertificateDirectory $resolvedCertificates `
        -BuildProvenancePath $provenancePath `
        -OutputPath $verificationRoot `
        -ReleaseToolPath (Join-Path $buildRoot 'bin\CP6.Platform.ReleaseTool\release\CP6.Platform.ReleaseTool.dll') | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Formal package-set verification failed.' }
    if ($InjectFailureAfterSigning) { throw 'Synthetic injected failure after formal verification.' }

    $completed = $true
    [pscustomobject]@{
        Version = $PackageVersion
        SourceGitSha = $SourceGitSha
        BuildInvocationId = "p10-s04:$SourceGitSha`:$RunId`:$RunAttempt"
        PackageCount = $packageIds.Count
        OutputPath = $resolvedOutput
        CertificateSha256 = $certificateSha256
    }
}
finally {
    if ($publicKey) { $publicKey.Dispose() }
    if ($certificate) { $certificate.Dispose() }
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    if ($spkiBytes) { [Array]::Clear($spkiBytes, 0, $spkiBytes.Length) }
    $password = $null
    Remove-Item Env:P10_NUGET_SIGNING_PFX_BASE64 -ErrorAction SilentlyContinue
    Remove-Item Env:P10_NUGET_SIGNING_PFX_PASSWORD -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $pfxPath) { Remove-Item -LiteralPath $pfxPath -Force }
    Remove-FormalDirectory $privateRoot $runnerPrefix
    if (-not $completed) {
        Remove-FormalDirectory $resolvedOutput $formalPrefix
    }
}
