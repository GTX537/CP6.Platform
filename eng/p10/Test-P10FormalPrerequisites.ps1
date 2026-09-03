[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Repository,

    [Parameter(Mandatory)]
    [string]$Environment,

    [Parameter(Mandatory)]
    [ValidatePattern('^0\.10\.0$')]
    [string]$PackageVersion,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedCommit,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$CheckoutCommit,

    [Parameter(Mandatory)]
    [string]$TrustPolicyPath,

    [Parameter(Mandatory)]
    [string]$CertificateDirectory,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$CrmTrustPolicySha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$SystemTrustPolicySha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$CrmCertificateSha256,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$SystemCertificateSha256,

    [switch]$UseProtectedEnvironmentSecretBinding,

    [string]$GitHubCliPath = 'gh',

    [string]$ReleaseToolPath,

    [string]$TimestampProbePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Repository -cne 'GTX537/CP6.Platform' -or $Environment -cne 'p10-formal-release') {
    throw 'Formal preflight repository or Environment is not approved.'
}
if ($UseProtectedEnvironmentSecretBinding -and $env:GITHUB_ACTIONS -cne 'true') {
    throw 'p10-signing-secrets: protected Environment binding mode is allowed only inside GitHub Actions.'
}
if ($env:GITHUB_REF -cne 'refs/heads/main' -or
    $env:GITHUB_SHA -cne $ExpectedCommit -or
    $CheckoutCommit -cne $ExpectedCommit) {
    throw 'p10-exact-main: event ref, event SHA, checkout SHA, and expected commit must match main.'
}
if ($env:S04_EXTERNAL_PREREQUISITES_READY -cne 'true') {
    throw 'p10-external-prerequisites: S04_EXTERNAL_PREREQUISITES_READY must be exactly true.'
}
if ([string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_BASE64) -or
    [string]::IsNullOrWhiteSpace($env:P10_NUGET_SIGNING_PFX_PASSWORD)) {
    throw 'p10-signing-secrets: both Environment signing Secrets are required.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resolvedPolicy = [IO.Path]::GetFullPath($TrustPolicyPath, $repositoryRoot)
$resolvedCertificates = [IO.Path]::GetFullPath($CertificateDirectory, $repositoryRoot)

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

function Invoke-GitHub([string[]]$Arguments, [switch]$AllowNotFound) {
    $result = Invoke-Injected $GitHubCliPath $Arguments
    if ($result.ExitCode -eq 0) { return $result.Output }
    if ($AllowNotFound -and $result.Error -match '(?i)(HTTP 404|not found)') { return '[]' }
    throw "GitHub CLI preflight failed for $($Arguments[0]) (exit $($result.ExitCode))."
}

function Invoke-ReleaseTool([string[]]$Arguments, [switch]$Capture) {
    if (-not [string]::IsNullOrWhiteSpace($ReleaseToolPath)) {
        $output = @(& dotnet ([IO.Path]::GetFullPath($ReleaseToolPath)) @Arguments 2>&1)
    }
    else {
        $project = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\CP6.Platform.ReleaseTool.csproj'
        $output = @(& dotnet run --project $project --configuration Release -- @Arguments 2>&1)
    }
    if ($LASTEXITCODE -ne 0) { throw "Release tool preflight failed for $($Arguments[0])." }
    if ($Capture) { return $output }
    $output | Out-Host
}

function Invoke-Rfc3161Probe {
    if (-not [string]::IsNullOrWhiteSpace($TimestampProbePath)) {
        $result = Invoke-Injected $TimestampProbePath @('http://timestamp.digicert.com')
        if ($result.ExitCode -ne 0) { throw 'Injected RFC3161 probe failed.' }
        $probe = $result.Output | ConvertFrom-Json -Depth 10
        if (-not $probe.success -or [string]::IsNullOrWhiteSpace([string]$probe.policyOid) -or @($probe.certificateChainSha256).Count -lt 1) {
            throw 'Injected RFC3161 probe did not return verified public identity.'
        }
        return $probe
    }

    $output = @(Invoke-ReleaseTool @('probe-rfc3161', 'http://timestamp.digicert.com') -Capture)
    $jsonLine = @($output | ForEach-Object { [string]$_ } | Where-Object { $_.TrimStart().StartsWith('{', [StringComparison]::Ordinal) } | Select-Object -Last 1)
    if ($jsonLine.Count -ne 1) { throw 'RFC3161 probe returned no verified identity.' }
    return $jsonLine[0] | ConvertFrom-Json -Depth 10
}

$visibility = (Invoke-GitHub @('repo', 'view', $Repository, '--json', 'visibility', '--jq', '.visibility')).Trim()
if ($visibility -cne 'PUBLIC') {
    throw 'p10-repository-visibility: CP6.Platform must remain PUBLIC.'
}
$mainSha = (Invoke-GitHub @('api', "/repos/$Repository/git/ref/heads/main", '--jq', '.object.sha')).Trim()
if ($mainSha -cne $ExpectedCommit) { throw 'p10-exact-main: remote main differs from the expected commit.' }

$environmentJson = Invoke-GitHub @('api', "/repos/$Repository/environments/$Environment")
$environmentState = $environmentJson | ConvertFrom-Json -Depth 20
$reviewerRules = @($environmentState.protection_rules | Where-Object { $_.type -ceq 'required_reviewers' })
if ($environmentState.name -cne $Environment -or
    $reviewerRules.Count -ne 1 -or
    @($reviewerRules[0].reviewers).Count -lt 1 -or
    $reviewerRules[0].prevent_self_review -ne $false -or
    $environmentState.deployment_branch_policy.custom_branch_policies -ne $true) {
    throw 'p10-environment-policy: required_reviewers with prevent_self_review=false and a custom main policy are required.'
}
$branchPolicyJson = Invoke-GitHub @('api', "/repos/$Repository/environments/$Environment/deployment-branch-policies")
$branchPolicies = @((($branchPolicyJson | ConvertFrom-Json -Depth 20).branch_policies))
if ($branchPolicies.Count -ne 1 -or $branchPolicies[0].name -cne 'main' -or $branchPolicies[0].type -cne 'branch') {
    throw 'p10-environment-policy: Environment deployment policy must allow only main.'
}

$secretInventoryMode = 'ProtectedEnvironmentBinding'
if (-not $UseProtectedEnvironmentSecretBinding) {
    $secretList = Invoke-GitHub @('secret', 'list', '--repo', $Repository, '--env', $Environment)
    $secretNames = @($secretList -split "`r?`n" | Where-Object { $_ } | ForEach-Object { ($_ -split '\s+')[0] } | Sort-Object)
    $expectedSecretNames = @('P10_NUGET_SIGNING_PFX_BASE64', 'P10_NUGET_SIGNING_PFX_PASSWORD') | Sort-Object
    if (($secretNames | ConvertTo-Json -Compress) -cne ($expectedSecretNames | ConvertTo-Json -Compress)) {
        throw 'p10-signing-secrets: Environment must contain exactly the two formal signing Secrets.'
    }
    $secretInventoryMode = 'ExactInventoryVerified'
}

$packageIds = @(
    'CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Contracts',
    'CP6.Platform.Deployment', 'CP6.Platform.EntityFramework', 'CP6.Platform.Messaging', 'CP6.Platform.Release'
)
foreach ($packageId in $packageIds) {
    $encoded = [Uri]::EscapeDataString($packageId)
    $versionsJson = Invoke-GitHub @(
        'api',
        "/users/GTX537/packages/nuget/$encoded/versions?per_page=100",
        '--paginate',
        '--slurp') -AllowNotFound
    $versions = @($versionsJson | ConvertFrom-Json -Depth 20)
    if (@($versions | Where-Object { $_.name -ceq $PackageVersion }).Count -ne 0) {
        throw "p10-package-version-conflict: $packageId/$PackageVersion already exists."
    }
}

Invoke-ReleaseTool @('validate-nuget-trust', $resolvedPolicy, $resolvedCertificates)
$policyHash = (Get-FileHash -LiteralPath $resolvedPolicy -Algorithm SHA256).Hash.ToLowerInvariant()
$policy = Get-Content -LiteralPath $resolvedPolicy -Raw | ConvertFrom-Json -Depth 20
$currentSigner = @($policy.signers | Where-Object { $_.status -ceq 'Current' })
if ($currentSigner.Count -ne 1) { throw 'p10-trust: expected one Current signer.' }
$certificateHash = [string]$currentSigner[0].certificateSha256
if ($policyHash -cne $CrmTrustPolicySha256 -or $policyHash -cne $SystemTrustPolicySha256 -or
    $certificateHash -cne $CrmCertificateSha256 -or $certificateHash -cne $SystemCertificateSha256) {
    throw 'p10-trust: Platform, CRM, and CP6 public trust hashes must be identical.'
}

$signerIdentity = & (Join-Path $PSScriptRoot 'Test-P10FormalSignerIdentity.ps1') `
    -TrustPolicyPath $resolvedPolicy `
    -CertificateDirectory $resolvedCertificates `
    -ReleaseToolPath $ReleaseToolPath
if ($signerIdentity.Status -cne 'Success' -or
    $signerIdentity.PolicySha256 -cne $policyHash -or
    $signerIdentity.CertificateSha256 -cne $certificateHash) {
    throw 'p10-trust: signer identity validation did not match the approved trust policy.'
}

$timestamp = Invoke-Rfc3161Probe
[pscustomobject]@{
    Status = 'Success'
    Version = $PackageVersion
    SourceGitSha = $ExpectedCommit
    RepositoryVisibility = $visibility
    Environment = $Environment
    SecretInventoryMode = $secretInventoryMode
    PolicySha256 = $policyHash
    CertificateSha256 = $certificateHash
    SignerSpkiKeyId = [string]$signerIdentity.SpkiKeyId
    SignerSubject = [string]$signerIdentity.Subject
    SignerValidFromUtc = [string]$signerIdentity.ValidFromUtc
    SignerValidUntilUtc = [string]$signerIdentity.ValidUntilUtc
    TimestampPolicyOid = [string]$timestamp.policyOid
    TimestampCertificateChainSha256 = @($timestamp.certificateChainSha256)
}
