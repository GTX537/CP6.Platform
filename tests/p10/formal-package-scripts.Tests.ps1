$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$bootstrapPath = Join-Path $repositoryRoot 'eng/p10/Initialize-P10FormalCertificate.ps1'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

Assert-True (Test-Path -LiteralPath $bootstrapPath -PathType Leaf) 'Formal certificate bootstrap script is missing.'
$bootstrap = Get-Content -LiteralPath $bootstrapPath -Raw
Assert-True ($bootstrap -notmatch 'Export-PfxCertificate') 'Bootstrap must not export a PFX through the certificate cmdlet.'
Assert-True ($bootstrap -notmatch '(?i)WriteAllBytes[^\r\n]*(pfx|pkcs12)') 'Bootstrap must not write PFX bytes.'
Assert-True ($bootstrap -notmatch '(?i)--body') 'Bootstrap must not place a Secret in gh arguments.'
Assert-True ($bootstrap -notmatch '(?i)Write-(Host|Output|Verbose|Debug)[^\r\n]*(password|pfx|secretvalue)') 'Bootstrap must not print secret material.'
Assert-True ($bootstrap -cmatch 'RSA\]::Create\(3072\)') 'Bootstrap must generate RSA-3072.'
Assert-True ($bootstrap -cmatch '\[byte\[\]\]::new\(16\)') 'Bootstrap must generate a 16-byte serial.'
Assert-True ($bootstrap -cmatch '\$serial\[0\] = \$serial\[0\] -bor 0x80') 'Bootstrap must set the serial high bit.'
Assert-True ($bootstrap -cmatch 'RedirectStandardInput = \$RedirectInput') 'Bootstrap must configure redirected standard input.'
Assert-True ($bootstrap -cmatch 'New-GitHubProcess \$arguments \$true') 'Secret writes must enable redirected standard input.'
Assert-True ($bootstrap -cmatch 'P10_NUGET_SIGNING_PFX_BASE64') 'Bootstrap must set the formal PFX Secret.'
Assert-True ($bootstrap -cmatch 'P10_NUGET_SIGNING_PFX_PASSWORD') 'Bootstrap must set the formal password Secret.'
Assert-True ($bootstrap -cmatch '\[Array\]::Clear') 'Bootstrap must zero sensitive byte arrays.'

$formalScriptPaths = @(
    'eng/p10/Pack-P10FormalPackages.ps1',
    'eng/p10/New-P10FormalPackageSet.ps1',
    'eng/p10/Test-P10FormalPackageSet.ps1'
)
foreach ($relativePath in $formalScriptPaths) {
    $path = Join-Path $repositoryRoot $relativePath
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$relativePath is missing."
    $text = Get-Content -LiteralPath $path -Raw
    Assert-True ($text -cmatch '(?m)^\[CmdletBinding\(\)\]\r?$') "$relativePath must declare CmdletBinding."
    Assert-True ($text -cmatch "Set-StrictMode -Version Latest") "$relativePath must enable strict mode."
    Assert-True ($text -notmatch 'CP6\.Platform\.Testing') "$relativePath must exclude the test helper package."
    Assert-True ($text -notmatch '(?i)--skip-duplicate') "$relativePath must not suppress version collisions."
}

$packFormal = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/Pack-P10FormalPackages.ps1') -Raw
$newFormal = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/New-P10FormalPackageSet.ps1') -Raw
$testFormal = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/Test-P10FormalPackageSet.ps1') -Raw
$verifyScript = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/verify.ps1') -Raw
$formalProjects = @(
    'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj',
    'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj',
    'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj',
    'src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj',
    'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj',
    'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj',
    'src/CP6.Platform.Release/CP6.Platform.Release.csproj'
)
foreach ($project in $formalProjects) {
    Assert-True ([regex]::Matches($packFormal, [regex]::Escape($project)).Count -eq 1) "Formal pack list must contain $project exactly once."
}
Assert-True ($packFormal -cmatch "\[ValidatePattern\('\^0\\\.10\\\.0\$'\)\]") 'Formal package version must be exactly stable 0.10.0.'
Assert-True ($packFormal -cmatch '(?s)dotnet pack.*--no-build.*--no-restore') 'Formal pack must reuse the one solution build.'
Assert-True ($packFormal -notmatch '(?i)nuget\s+push') 'Formal packing must not publish packages.'
Assert-True ([regex]::Matches($newFormal, '(?m)^\s*& dotnet restore ').Count -eq 1) 'Formal orchestration must restore exactly once.'
Assert-True ([regex]::Matches($newFormal, '(?m)^\s*& dotnet build ').Count -eq 1) 'Formal orchestration must build exactly once.'
Assert-True ($newFormal -cmatch 'EphemeralKeySet') 'Formal signing must load the PFX with EphemeralKeySet.'
Assert-True ($newFormal -cmatch '\$env:RUNNER_TEMP') 'Formal signing material must stay below RUNNER_TEMP.'
Assert-True ($newFormal -cmatch '::add-mask::') 'Formal signing must mask the exact child-process password.'
Assert-True ($newFormal -cmatch "--timestamper 'http://timestamp\.digicert\.com'") 'Formal signing must use the fixed timestamp service.'
Assert-True ($newFormal -cmatch '--hash-algorithm SHA256') 'Formal signing must use SHA-256.'
Assert-True ($newFormal -cmatch '--timestamp-hash-algorithm SHA256') 'Formal timestamping must use SHA-256.'
Assert-True ($newFormal -cmatch '(?s)finally\s*\{.*\[Array\]::Clear.*Remove-Item') 'Formal orchestration must zero and remove secret state in finally.'
Assert-True ($testFormal -cmatch "'verify-formal-package'") 'Formal verification must use the independent package verifier.'
Assert-True ($testFormal -cmatch "'Current'") 'Formal verification must require Current trust mode.'
Assert-True ($testFormal -cmatch "'validate-build-provenance'") 'Formal verification must validate build provenance.'
Assert-True ($verifyScript -cmatch "P10FormalPackageScriptContracts") 'The Contract gate must run formal script contracts.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p10-formal-bootstrap-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$originalState = [Environment]::GetEnvironmentVariable('CP6_TEST_GH_STATE')
$originalFailure = [Environment]::GetEnvironmentVariable('CP6_TEST_GH_FAIL_WRITE')
$originalNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')
$originalPfxBase64 = [Environment]::GetEnvironmentVariable('P10_NUGET_SIGNING_PFX_BASE64')
$originalPfxPassword = [Environment]::GetEnvironmentVariable('P10_NUGET_SIGNING_PFX_PASSWORD')
$originalRunnerTemp = [Environment]::GetEnvironmentVariable('RUNNER_TEMP')

try {
    $fakeGh = Join-Path $testRoot 'fake-gh.ps1'
    $fakeGhText = @'
[CmdletBinding()]
param([Parameter(ValueFromRemainingArguments)][string[]]$GhArguments)
$ErrorActionPreference = 'Stop'
$statePath = $env:CP6_TEST_GH_STATE
if ($GhArguments.Count -ge 2 -and $GhArguments[0] -ceq 'secret' -and $GhArguments[1] -ceq 'set') {
    $value = [Console]::In.ReadToEnd()
    [IO.File]::AppendAllText($statePath, $value.Length.ToString([Globalization.CultureInfo]::InvariantCulture) + [Environment]::NewLine)
    $count = [IO.File]::ReadAllLines($statePath).Count
    if ($env:CP6_TEST_GH_FAIL_WRITE -and $count -eq [int]$env:CP6_TEST_GH_FAIL_WRITE) { exit 19 }
    exit 0
}
if ($GhArguments.Count -ge 2 -and $GhArguments[0] -ceq 'secret' -and $GhArguments[1] -ceq 'list') {
    'P10_NUGET_SIGNING_PFX_BASE64'
    'P10_NUGET_SIGNING_PFX_PASSWORD'
    exit 0
}
exit 23
'@
    [IO.File]::WriteAllText($fakeGh, $fakeGhText, [Text.UTF8Encoding]::new($false))

    $env:MSBUILDDISABLENODEREUSE = '1'
    $toolArtifacts = Join-Path $testRoot 'tool-artifacts'
    & dotnet build (Join-Path $repositoryRoot 'tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj') `
        --configuration Release "-p:ArtifactsPath=$toolArtifacts" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Release tool build failed.' }
    $releaseToolPath = Join-Path $toolArtifacts 'bin/CP6.Platform.ReleaseTool/release/CP6.Platform.ReleaseTool.dll'
    Assert-True (Test-Path -LiteralPath $releaseToolPath -PathType Leaf) 'Isolated Release tool was not produced.'

    $successRoot = Join-Path $testRoot 'success-repository'
    $successState = Join-Path $testRoot 'success-lengths.txt'
    $env:CP6_TEST_GH_STATE = $successState
    $env:CP6_TEST_GH_FAIL_WRITE = $null
    & pwsh -NoProfile -File $bootstrapPath `
        -Repository 'GTX537/CP6.Platform' `
        -Environment 'p10-formal-release' `
        -RepositoryRoot $successRoot `
        -GitHubCliPath $fakeGh `
        -ReleaseToolPath $releaseToolPath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap success case failed.' }
    $lengths = [IO.File]::ReadAllLines($successState)
    Assert-True ($lengths.Count -eq 2) 'Fake gh must observe exactly two Secret writes.'
    Assert-True (@($lengths | Where-Object { $_ -notmatch '^[1-9][0-9]*$' }).Count -eq 0) 'Fake gh may record only input lengths.'
    Assert-True ([int]$lengths[0] -gt 1000) 'The first standard-input value must be a non-empty PFX payload.'
    Assert-True ([int]$lengths[1] -eq 44) 'The second standard-input value must be a 32-byte base64 password.'
    Assert-True (@(Get-ChildItem -LiteralPath (Join-Path $successRoot 'eng/p10/trust/certificates') -Filter '*.cer' -File).Count -eq 1) 'Success must retain exactly one public CER.'
    Assert-True (Test-Path -LiteralPath (Join-Path $successRoot 'eng/p10/trust/p10-formal-nuget-trust-store.v1.json') -PathType Leaf) 'Success must retain the canonical trust policy.'

    $failureRoot = Join-Path $testRoot 'failure-repository'
    $failureState = Join-Path $testRoot 'failure-lengths.txt'
    $env:CP6_TEST_GH_STATE = $failureState
    $env:CP6_TEST_GH_FAIL_WRITE = '2'
    $failureOutput = & pwsh -NoProfile -File $bootstrapPath `
        -Repository 'GTX537/CP6.Platform' `
        -Environment 'p10-formal-release' `
        -RepositoryRoot $failureRoot `
        -GitHubCliPath $fakeGh `
        -ReleaseToolPath $releaseToolPath 2>&1 | Out-String
    Assert-True ($LASTEXITCODE -ne 0) 'Injected second Secret write must fail.'
    Assert-True ($failureOutput -match '(?i)rotate') 'Partial Secret failure must instruct rotation.'
    $failureLengths = [IO.File]::ReadAllLines($failureState)
    Assert-True ($failureLengths.Count -eq 2) 'Injected failure must occur on the second Secret write.'
    Assert-True (@($failureLengths | Where-Object { $_ -notmatch '^[1-9][0-9]*$' }).Count -eq 0) 'Failure logging may record only input lengths.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $failureRoot 'eng/p10/trust/p10-formal-nuget-trust-store.v1.json'))) 'Failure must not retain a public policy half-state.'
    Assert-True (-not (Test-Path -LiteralPath (Join-Path $failureRoot 'eng/p10/trust/certificates'))) 'Failure must not retain a public certificate half-state.'

    # Synthetic-only execution proof: sign and verify all seven packages, then fail before acceptance.
    $syntheticTrust = Join-Path $testRoot 'synthetic-trust'
    $syntheticCertificates = Join-Path $syntheticTrust 'certificates'
    [IO.Directory]::CreateDirectory($syntheticCertificates) | Out-Null
    $syntheticRsa = [Security.Cryptography.RSA]::Create(3072)
    $syntheticCertificate = $null
    $syntheticPublicKey = $null
    $syntheticPasswordBytes = $null
    $syntheticPfxBytes = $null
    $syntheticDer = $null
    $syntheticSpki = $null
    try {
        $syntheticNow = [DateTimeOffset]::UtcNow
        $syntheticRequest = [Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=CP6 Platform Release Signing',
            $syntheticRsa,
            [Security.Cryptography.HashAlgorithmName]::SHA256,
            [Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $syntheticRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false, $false, 0, $true)) | Out-Null
        $syntheticRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new(
                [Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,
                $true)) | Out-Null
        $syntheticEku = [Security.Cryptography.OidCollection]::new()
        $syntheticEku.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3')) | Out-Null
        $syntheticRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($syntheticEku, $false)) | Out-Null
        $syntheticRequest.CertificateExtensions.Add(
            [Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($syntheticRequest.PublicKey, $false)) | Out-Null
        $syntheticCertificate = $syntheticRequest.CreateSelfSigned($syntheticNow.AddMinutes(-5), $syntheticNow.AddDays(730))
        $syntheticDer = $syntheticCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
        $syntheticFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($syntheticDer)).ToLowerInvariant()
        [IO.File]::WriteAllBytes((Join-Path $syntheticCertificates "$syntheticFingerprint.cer"), $syntheticDer)
        $syntheticPublicKey = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($syntheticCertificate)
        $syntheticSpki = $syntheticPublicKey.ExportSubjectPublicKeyInfo()
        $syntheticSpkiId = 'sha256:' + [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($syntheticSpki)).ToLowerInvariant()
        $syntheticPasswordBytes = [byte[]]::new(32)
        [Security.Cryptography.RandomNumberGenerator]::Fill($syntheticPasswordBytes)
        $syntheticPassword = [Convert]::ToBase64String($syntheticPasswordBytes)
        $syntheticPfxBytes = $syntheticCertificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12, $syntheticPassword)
        $env:P10_NUGET_SIGNING_PFX_BASE64 = [Convert]::ToBase64String($syntheticPfxBytes)
        $env:P10_NUGET_SIGNING_PFX_PASSWORD = $syntheticPassword

        $formatUtc = { param([DateTimeOffset]$Value) $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture) }
        $syntheticPolicy = [ordered]@{
            '$schemaId' = 'https://schemas.cp6.dev/release/pinned-nuget-trust-store.v1'
            policyVersion = 1
            trustModel = 'PinnedSelfSigned'
            publicCaTrusted = $false
            internallyTrusted = $true
            timestampPolicy = 'Rfc3161Required'
            timestampService = 'http://timestamp.digicert.com'
            allowedPackageIds = @(
                'CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Contracts',
                'CP6.Platform.Deployment', 'CP6.Platform.EntityFramework', 'CP6.Platform.Messaging', 'CP6.Platform.Release'
            )
            signers = @([ordered]@{
                certificatePath = "certificates/$syntheticFingerprint.cer"
                certificateSha256 = $syntheticFingerprint
                spkiKeyId = $syntheticSpkiId
                subject = $syntheticCertificate.SubjectName.Name
                issuer = $syntheticCertificate.IssuerName.Name
                validFromUtc = & $formatUtc ([DateTimeOffset]::new($syntheticCertificate.NotBefore.ToUniversalTime()))
                validUntilUtc = & $formatUtc ([DateTimeOffset]::new($syntheticCertificate.NotAfter.ToUniversalTime()))
                status = 'Current'
                activatedAtUtc = & $formatUtc $syntheticNow
                revokedAtUtc = $null
                revocationReason = $null
            })
        }
        $syntheticRawPolicy = Join-Path $syntheticTrust 'synthetic-policy.raw.json'
        $syntheticPolicyPath = Join-Path $syntheticTrust 'p10-formal-nuget-trust-store.v1.json'
        [IO.File]::WriteAllText($syntheticRawPolicy, ($syntheticPolicy | ConvertTo-Json -Depth 20 -Compress), [Text.UTF8Encoding]::new($false))
        & dotnet $releaseToolPath canonicalize $syntheticRawPolicy $syntheticPolicyPath | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic policy canonicalization failed.' }
        Remove-Item -LiteralPath $syntheticRawPolicy -Force
        & dotnet $releaseToolPath validate-nuget-trust $syntheticPolicyPath $syntheticCertificates | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Synthetic policy validation failed.' }

        $syntheticRunnerTemp = Join-Path $testRoot 'synthetic-runner-temp'
        [IO.Directory]::CreateDirectory($syntheticRunnerTemp) | Out-Null
        $env:RUNNER_TEMP = $syntheticRunnerTemp
        $sourceGitSha = (& git -C $repositoryRoot rev-parse HEAD).Trim()
        $syntheticOutput = Join-Path $repositoryRoot ('artifacts/p10-formal/synthetic-' + [Guid]::NewGuid().ToString('N'))
        $syntheticOutputText = & pwsh -NoProfile -File (Join-Path $repositoryRoot 'eng/p10/New-P10FormalPackageSet.ps1') `
            -SourceGitSha $sourceGitSha `
            -RunId 1 `
            -RunAttempt 1 `
            -PackageVersion '0.10.0' `
            -OutputPath $syntheticOutput `
            -TrustPolicyPath $syntheticPolicyPath `
            -CertificateDirectory $syntheticCertificates `
            -InjectFailureAfterSigning 2>&1 | Out-String
        Assert-True ($LASTEXITCODE -ne 0) 'Synthetic injected post-signing failure must fail.'
        Assert-True ($syntheticOutputText -match 'Synthetic injected failure') 'Synthetic execution must reach the post-signing failure point.'
        Assert-True (-not (Test-Path -LiteralPath $syntheticOutput)) 'Synthetic failure must remove all candidate formal output.'
        $syntheticPrivateResidue = @(Get-ChildItem -LiteralPath $syntheticRunnerTemp -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
            $_.Extension -in '.pfx', '.p12', '.pem', '.key' -or $_.Name -match '(?i)password|private[-_]?key'
        })
        Assert-True ($syntheticPrivateResidue.Count -eq 0) 'Synthetic failure must remove all private runner residue.'
    }
    finally {
        if ($syntheticPasswordBytes) { [Array]::Clear($syntheticPasswordBytes, 0, $syntheticPasswordBytes.Length) }
        if ($syntheticPfxBytes) { [Array]::Clear($syntheticPfxBytes, 0, $syntheticPfxBytes.Length) }
        if ($syntheticSpki) { [Array]::Clear($syntheticSpki, 0, $syntheticSpki.Length) }
        if ($syntheticPublicKey) { $syntheticPublicKey.Dispose() }
        if ($syntheticCertificate) { $syntheticCertificate.Dispose() }
        $syntheticRsa.Dispose()
    }

    foreach ($root in @($successRoot, $failureRoot)) {
        $privateResidue = @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue | Where-Object {
            $_.Extension -in '.pfx', '.p12', '.pem', '.key' -or $_.Name -match '(?i)password|private[-_]?key'
        })
        Assert-True ($privateResidue.Count -eq 0) "Private material remained under $root."
    }
}
finally {
    [Environment]::SetEnvironmentVariable('CP6_TEST_GH_STATE', $originalState)
    [Environment]::SetEnvironmentVariable('CP6_TEST_GH_FAIL_WRITE', $originalFailure)
    [Environment]::SetEnvironmentVariable('MSBUILDDISABLENODEREUSE', $originalNodeReuse)
    [Environment]::SetEnvironmentVariable('P10_NUGET_SIGNING_PFX_BASE64', $originalPfxBase64)
    [Environment]::SetEnvironmentVariable('P10_NUGET_SIGNING_PFX_PASSWORD', $originalPfxPassword)
    [Environment]::SetEnvironmentVariable('RUNNER_TEMP', $originalRunnerTemp)
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'P10 formal package script tests passed.'
