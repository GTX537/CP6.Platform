[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$Environment,

    [string]$RepositoryRoot,

    [string]$GitHubCliPath = 'gh',

    [string]$ReleaseToolPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Repository -cne 'GTX537/CP6.Platform') {
    throw 'Formal signing bootstrap is restricted to GTX537/CP6.Platform.'
}
if ($Environment -cne 'p10-formal-release') {
    throw 'Formal signing bootstrap is restricted to p10-formal-release.'
}
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..\..'
}

$resolvedRepositoryRoot = [IO.Path]::GetFullPath($RepositoryRoot)
$trustRoot = [IO.Path]::GetFullPath((Join-Path $resolvedRepositoryRoot 'eng\p10\trust'))
$trustPrefix = $trustRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$certificateDirectory = Join-Path $trustRoot 'certificates'
$policyPath = Join-Path $trustRoot 'p10-formal-nuget-trust-store.v1.json'
if (Test-Path -LiteralPath $policyPath) {
    throw 'Formal trust policy already exists; use an explicit rotation procedure.'
}
if ((Test-Path -LiteralPath $certificateDirectory) -and @(Get-ChildItem -LiteralPath $certificateDirectory -Force).Count -ne 0) {
    throw 'Formal certificate directory is not empty; use an explicit rotation procedure.'
}

$stagingRoot = [IO.Path]::GetFullPath((Join-Path $trustRoot ('.bootstrap-' + [Guid]::NewGuid().ToString('N'))))
if (-not $stagingRoot.StartsWith($trustPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Bootstrap staging path escaped the formal trust root.'
}
$stagingCertificateDirectory = Join-Path $stagingRoot 'certificates'
$stagingRawPolicy = Join-Path $stagingRoot 'policy.raw.json'
$stagingPolicy = Join-Path $stagingRoot 'p10-formal-nuget-trust-store.v1.json'

function New-GitHubProcess([string[]]$Arguments, [bool]$RedirectInput) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    if ($GitHubCliPath.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $startInfo.FileName = 'pwsh'
        $startInfo.ArgumentList.Add('-NoProfile')
        $startInfo.ArgumentList.Add('-File')
        $startInfo.ArgumentList.Add([IO.Path]::GetFullPath($GitHubCliPath))
    }
    else {
        $startInfo.FileName = $GitHubCliPath
    }
    foreach ($argument in $Arguments) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    $startInfo.RedirectStandardInput = $RedirectInput
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    return $startInfo
}

function Set-GitHubEnvironmentSecret([string]$Name, [string]$SecretValue) {
    $arguments = @('secret', 'set', $Name, '--repo', $Repository, '--env', $Environment)
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = New-GitHubProcess $arguments $true
        if (-not $process.Start()) { throw "Unable to start GitHub CLI for $Name." }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.StandardInput.Write($SecretValue)
        $process.StandardInput.Close()
        $process.WaitForExit()
        $null = $standardOutput.GetAwaiter().GetResult()
        $null = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "GitHub CLI failed while setting $Name (exit $($process.ExitCode))."
        }
    }
    finally {
        $process.Dispose()
    }
}

function Get-GitHubEnvironmentSecretNames {
    $arguments = @('secret', 'list', '--repo', $Repository, '--env', $Environment)
    $process = [Diagnostics.Process]::new()
    try {
        $process.StartInfo = New-GitHubProcess $arguments $false
        if (-not $process.Start()) { throw 'Unable to start GitHub CLI for Secret verification.' }
        $standardOutput = $process.StandardOutput.ReadToEndAsync()
        $standardError = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $output = $standardOutput.GetAwaiter().GetResult()
        $null = $standardError.GetAwaiter().GetResult()
        if ($process.ExitCode -ne 0) {
            throw "GitHub CLI failed while listing Environment Secrets (exit $($process.ExitCode))."
        }
        return $output
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-ReleaseTool([string[]]$Arguments) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        $resolvedTool = [IO.Path]::GetFullPath($ReleaseToolPath)
        if (-not (Test-Path -LiteralPath $resolvedTool -PathType Leaf)) {
            throw 'The supplied Release tool does not exist.'
        }
        & dotnet $resolvedTool @Arguments | Out-Host
    }
    else {
        $project = Join-Path $resolvedRepositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        if (-not (Test-Path -LiteralPath $project -PathType Leaf)) {
            throw 'The Release tool project does not exist below RepositoryRoot.'
        }
        & dotnet run --project $project --configuration Release -- @Arguments | Out-Host
    }
    if ($LASTEXITCODE -ne 0) {
        throw "Release tool failed with exit code $LASTEXITCODE."
    }
}

function Format-UtcMilliseconds([DateTimeOffset]$Value) {
    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
}

function Remove-BootstrapDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not $resolved.StartsWith($trustPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a directory outside the formal trust root.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}

$rsa = $null
$issued = $null
$certificate = $null
$publicKey = $null
$passwordBytes = $null
$pfxBytes = $null
$derBytes = $null
$spkiBytes = $null
$password = $null
$pfxBase64 = $null
$secretWriteCount = 0
$finalCertificateDirectoryCreated = $false
$finalPolicyCreated = $false

try {
    [IO.Directory]::CreateDirectory($stagingCertificateDirectory) | Out-Null
    $now = [DateTimeOffset]::UtcNow
    $rsa = [Security.Cryptography.RSA]::Create(3072)
    $subjectName = [Security.Cryptography.X509Certificates.X500DistinguishedName]::new('CN=CP6 Platform Release Signing')
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
        $subjectName,
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
        [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($enhancedKeyUsages, $false)) | Out-Null
    $request.CertificateExtensions.Add(
        [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey, $false)) | Out-Null

    $serial = [byte[]]::new(16)
    [Security.Cryptography.RandomNumberGenerator]::Fill($serial)
    $serial[0] = $serial[0] -bor 0x80
    $signatureGenerator = [Security.Cryptography.X509Certificates.X509SignatureGenerator]::CreateForRSA(
        $rsa,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $issued = $request.Create($subjectName, $signatureGenerator, $now.AddMinutes(-5), $now.AddDays(730), $serial)
    $certificate = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::CopyWithPrivateKey($issued, $rsa)

    $passwordBytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($passwordBytes)
    $password = [Convert]::ToBase64String($passwordBytes)
    $pfxBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $password)
    $pfxBase64 = [Convert]::ToBase64String($pfxBytes)
    $derBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $certificateSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($derBytes)).ToLowerInvariant()
    $publicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($certificate)
    $spkiBytes = $publicKey.ExportSubjectPublicKeyInfo()
    $spkiKeyId = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($spkiBytes)).ToLowerInvariant()
    $certificatePath = Join-Path $stagingCertificateDirectory "$certificateSha256.cer"
    [IO.File]::WriteAllBytes($certificatePath, $derBytes)

    $validFromUtc = [DateTimeOffset]::new($certificate.NotBefore.ToUniversalTime())
    $validUntilUtc = [DateTimeOffset]::new($certificate.NotAfter.ToUniversalTime())
    $policy = [ordered]@{
        '$schemaId' = 'https://schemas.cp6.dev/release/pinned-nuget-trust-store.v1'
        policyVersion = 1
        trustModel = 'PinnedSelfSigned'
        publicCaTrusted = $false
        internallyTrusted = $true
        timestampPolicy = 'Rfc3161Required'
        timestampService = 'http://timestamp.digicert.com'
        allowedPackageIds = @(
            'CP6.Platform.Abstractions',
            'CP6.Platform.AspNetCore',
            'CP6.Platform.Contracts',
            'CP6.Platform.Deployment',
            'CP6.Platform.EntityFramework',
            'CP6.Platform.Messaging',
            'CP6.Platform.Release'
        )
        signers = @(
            [ordered]@{
                certificatePath = "certificates/$certificateSha256.cer"
                certificateSha256 = $certificateSha256
                spkiKeyId = $spkiKeyId
                subject = $certificate.SubjectName.Name
                issuer = $certificate.IssuerName.Name
                validFromUtc = Format-UtcMilliseconds $validFromUtc
                validUntilUtc = Format-UtcMilliseconds $validUntilUtc
                status = 'Current'
                activatedAtUtc = Format-UtcMilliseconds $now
                revokedAtUtc = $null
                revocationReason = $null
            }
        )
    }
    [IO.File]::WriteAllText(
        $stagingRawPolicy,
        ($policy | ConvertTo-Json -Depth 12 -Compress),
        [Text.UTF8Encoding]::new($false))
    Invoke-ReleaseTool @('canonicalize', $stagingRawPolicy, $stagingPolicy)
    Remove-Item -LiteralPath $stagingRawPolicy -Force
    Invoke-ReleaseTool @('validate-nuget-trust', $stagingPolicy, $stagingCertificateDirectory)

    Set-GitHubEnvironmentSecret 'P10_NUGET_SIGNING_PFX_BASE64' $pfxBase64
    $secretWriteCount++
    Set-GitHubEnvironmentSecret 'P10_NUGET_SIGNING_PFX_PASSWORD' $password
    $secretWriteCount++
    $secretNames = Get-GitHubEnvironmentSecretNames
    foreach ($name in @('P10_NUGET_SIGNING_PFX_BASE64', 'P10_NUGET_SIGNING_PFX_PASSWORD')) {
        if ($secretNames -notmatch "(?m)^$([regex]::Escape($name))(?:\s|$)") {
            throw "Environment Secret $name was not confirmed."
        }
    }

    [IO.Directory]::CreateDirectory($trustRoot) | Out-Null
    if (Test-Path -LiteralPath $certificateDirectory) {
        Remove-Item -LiteralPath $certificateDirectory -Force
    }
    [IO.Directory]::Move($stagingCertificateDirectory, $certificateDirectory)
    $finalCertificateDirectoryCreated = $true
    [IO.File]::Move($stagingPolicy, $policyPath)
    $finalPolicyCreated = $true

    $policySha256 = [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($policyPath))).ToLowerInvariant()
    [pscustomobject]@{
        ConfirmedSecretNames = @('P10_NUGET_SIGNING_PFX_BASE64', 'P10_NUGET_SIGNING_PFX_PASSWORD')
        CertificateSha256 = $certificateSha256
        SpkiKeyId = $spkiKeyId
        PolicySha256 = $policySha256
        ValidFromUtc = Format-UtcMilliseconds $validFromUtc
        ValidUntilUtc = Format-UtcMilliseconds $validUntilUtc
        CertificatePath = "eng/p10/trust/certificates/$certificateSha256.cer"
    }
}
catch {
    if ($finalPolicyCreated -and (Test-Path -LiteralPath $policyPath)) {
        Remove-Item -LiteralPath $policyPath -Force
    }
    if ($finalCertificateDirectoryCreated) {
        Remove-BootstrapDirectory $certificateDirectory
    }
    if ($secretWriteCount -gt 0) {
        throw [InvalidOperationException]::new(
            'Bootstrap failed after a Secret write. Rotate both formal signing Secrets before retrying; the generated value cannot be reconstructed.',
            $_.Exception)
    }
    throw
}
finally {
    Remove-BootstrapDirectory $stagingRoot
    if ($passwordBytes) { [Array]::Clear($passwordBytes, 0, $passwordBytes.Length) }
    if ($pfxBytes) { [Array]::Clear($pfxBytes, 0, $pfxBytes.Length) }
    if ($spkiBytes) { [Array]::Clear($spkiBytes, 0, $spkiBytes.Length) }
    $password = $null
    $pfxBase64 = $null
    if ($publicKey) { $publicKey.Dispose() }
    if ($certificate) { $certificate.Dispose() }
    if ($issued) { $issued.Dispose() }
    if ($rsa) { $rsa.Dispose() }
}
