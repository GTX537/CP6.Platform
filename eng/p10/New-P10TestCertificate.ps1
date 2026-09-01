[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'P10 test certificate creation requires Windows.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Certificate output must be below $artifactsRoot."
}
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$now = [DateTimeOffset]::UtcNow
$subject = 'CN=CP6 Platform P10 TEST ONLY'
$rsa = [Security.Cryptography.RSA]::Create(2048)
$request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
    $subject,
    $rsa,
    [Security.Cryptography.HashAlgorithmName]::SHA256,
    [Security.Cryptography.RSASignaturePadding]::Pkcs1)
$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true)) | Out-Null
$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
        [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
        $true)) | Out-Null
$enhancedKeyUsages = [Security.Cryptography.OidCollection]::new()
$enhancedKeyUsages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($enhancedKeyUsages, $true)) | Out-Null
$request.CertificateExtensions.Add(
    [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false)) | Out-Null

$certificate = $request.CreateSelfSigned($now.AddMinutes(-5), $now.AddDays(91))
$passwordBytes = [byte[]]::new(32)
[Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
$password = [Convert]::ToBase64String($passwordBytes)
[Array]::Clear($passwordBytes, 0, $passwordBytes.Length)

$pfxPath = Join-Path $resolvedOutput 'test-signing-private.pfx'
$cerPath = Join-Path $resolvedOutput 'test-signing-public.cer'
[IO.File]::WriteAllBytes($pfxPath, $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password))
$cerBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
[IO.File]::WriteAllBytes($cerPath, $cerBytes)
$fingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($cerBytes)).ToLowerInvariant()
$certificateSubject = $certificate.Subject

$result = [pscustomobject]@{
    PfxPath = $pfxPath
    CerPath = $cerPath
    Password = $password
    Fingerprint = $fingerprint
    CertificateSubject = $certificateSubject
    TrustMode = 'PinnedNuGetVerifierAllowUntrustedRoot'
}
$certificate.Dispose()
$rsa.Dispose()
$result
