[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$TrustPolicyPath,

    [Parameter(Mandatory)]
    [string]$CertificateDirectory,

    [string]$ReleaseToolPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_PASSWORD)) {
    throw 'p10-signing-secrets: both Environment signing Secrets are required.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)

function Invoke-ReleaseTool([string[]]$Arguments) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        $output = @(& dotnet ([IO.Path]::GetFullPath($ReleaseToolPath)) @Arguments 2>&1)
    }
    else {
        $project = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        $output = @(& dotnet run --project $project --configuration Release -- @Arguments 2>&1)
    }
    if ($LASTEXITCODE -ne 0) { throw "Release tool signer validation failed for $($Arguments[0])." }
    $output | Out-Host
}

function Format-CertificateUtc([DateTime]$Value) {
    return ([DateTimeOffset]::new($Value.ToUniversalTime())).ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
        [Globalization.CultureInfo]::InvariantCulture)
}

Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)
$policyHash = (Get-FileHash -LiteralPath $resolvedPolicy -Algorithm SHA256).Hash.ToLowerInvariant()
$policy = Get-Content -LiteralPath $resolvedPolicy -Raw | ConvertFrom-Json -Depth 20
$currentSigner = @($policy.signers | Where-Object { $_.status -ceq 'Current' })
if ($currentSigner.Count -ne 1) { throw 'p10-trust: expected one Current signer.' }

$expectedCertificateHash = [string]$currentSigner[0].certificateSha256
$expectedSpki = [string]$currentSigner[0].spkiKeyId
$policyDirectory = Split-Path -Parent $resolvedPolicy
$resolvedPublicCertificate = [IO.Path]::GetFullPath([string]$currentSigner[0].certificatePath, $policyDirectory)
$certificateRoot = $resolvedCertificates.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedPublicCertificate.StartsWith($certificateRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'p10-trust: Current signer certificate escaped the approved certificate directory.'
}
if (-not (Test-Path -LiteralPath $resolvedPublicCertificate -PathType Leaf)) {
    throw 'p10-trust: Current signer public certificate is missing.'
}

$publicCertificateBytes = [IO.File]::ReadAllBytes($resolvedPublicCertificate)
$publicCertificateHash = [Convert]::ToHexString(
    [Security.Cryptography.SHA256]::HashData($publicCertificateBytes)).ToLowerInvariant()
if ($publicCertificateHash -cne $expectedCertificateHash) {
    throw 'p10-trust: Current signer public certificate fingerprint differs from policy.'
}

$pfxBytes = $null
$pfxCertificateDer = $null
$spkiBytes = $null
$pfxCertificate = $null
$publicCertificate = $null
$publicKey = $null
try {
    try {
        $pfxBytes = [Convert]::FromBase64String($env:P10_NUGET_SIGNING_PFX_BASE64)
    }
    catch {
        throw 'p10-trust: Environment PFX is not valid base64.'
    }

    try {
        $pfxCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $pfxBytes,
            $env:P10_NUGET_SIGNING_PFX_PASSWORD,
            [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::EphemeralKeySet)
    }
    catch {
        throw 'p10-trust: Environment PFX could not be opened with the protected password.'
    }
    if (-not $pfxCertificate.HasPrivateKey) { throw 'p10-trust: Environment PFX has no private key.' }

    $publicCertificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($publicCertificateBytes)
    if ($publicCertificate.HasPrivateKey) { throw 'p10-trust: committed certificate unexpectedly contains a private key.' }

    $pfxCertificateDer = $pfxCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    if ($pfxCertificateDer.Length -ne $publicCertificateBytes.Length -or
        -not [Security.Cryptography.CryptographicOperations]::FixedTimeEquals($pfxCertificateDer, $publicCertificateBytes)) {
        throw 'p10-trust: Environment PFX public DER differs from the committed Current signer.'
    }

    $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($pfxCertificate)
    if ($null -eq $publicKey) { throw 'p10-trust: Environment PFX signer is not RSA.' }
    $spkiBytes = $publicKey.ExportSubjectPublicKeyInfo()
    $pfxSpki = 'sha256:' + [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($spkiBytes)).ToLowerInvariant()
    if ($pfxSpki -cne $expectedSpki) { throw 'p10-trust: Environment PFX SPKI differs from policy.' }

    $validFromUtc = Format-CertificateUtc $pfxCertificate.NotBefore
    $validUntilUtc = Format-CertificateUtc $pfxCertificate.NotAfter
    $expectedValidFromUtc = ([DateTimeOffset]$currentSigner[0].validFromUtc).ToUniversalTime().ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
        [Globalization.CultureInfo]::InvariantCulture)
    $expectedValidUntilUtc = ([DateTimeOffset]$currentSigner[0].validUntilUtc).ToUniversalTime().ToString(
        "yyyy-MM-dd'T'HH:mm:ss.fff'Z'",
        [Globalization.CultureInfo]::InvariantCulture)
    if ($pfxCertificate.SubjectName.Name -cne [string]$currentSigner[0].subject) {
        throw 'p10-trust: Environment PFX subject differs from policy.'
    }
    if ($pfxCertificate.IssuerName.Name -cne [string]$currentSigner[0].issuer) {
        throw 'p10-trust: Environment PFX issuer differs from policy.'
    }
    if ($validFromUtc -cne $expectedValidFromUtc -or
        $validUntilUtc -cne $expectedValidUntilUtc) {
        throw 'p10-trust: Environment PFX validity differs from policy.'
    }

    [pscustomobject]@{
        Status = 'Success'
        PolicySha256 = $policyHash
        CertificateSha256 = $publicCertificateHash
        SpkiKeyId = $pfxSpki
        Subject = $pfxCertificate.SubjectName.Name
        Issuer = $pfxCertificate.IssuerName.Name
        ValidFromUtc = $validFromUtc
        ValidUntilUtc = $validUntilUtc
        CertificatePath = [string]$currentSigner[0].certificatePath
        PublicDerMatch = $true
    }
}
finally {
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    if ($pfxCertificateDer) { [Array]::Clear($pfxCertificateDer, 0, $pfxCertificateDer.Length) }
    if ($spkiBytes) { [Array]::Clear($spkiBytes, 0, $spkiBytes.Length) }
    if ($publicKey) { $publicKey.Dispose() }
    if ($publicCertificate) { $publicCertificate.Dispose() }
    if ($pfxCertificate) { $pfxCertificate.Dispose() }
}
