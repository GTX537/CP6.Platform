[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SourceGitSha,
    [Parameter(Mandatory)][string]$WorkflowRunId,
    [Parameter(Mandatory)][ValidateRange(1, [int]::MaxValue)][int]$WorkflowRunAttempt,
    [string]$WorkflowJob = 'publish',
    [string]$PackageDirectory = 'artifacts/p09-package',
    [string]$VerificationDirectory = 'artifacts/verify',
    [string]$P05ResultPath = 'artifacts/p05-integration/result.json',
    [string]$P06ResultPath = 'artifacts/p06-sql-integration/result.json',
    [string]$RehearsalDirectory = 'artifacts/p09-rehearsal',
    [string]$KubernetesDirectory = 'artifacts/p09-kubernetes',
    [string]$OutputPath = 'artifacts/p09-publication/candidate-manifest.v1.json'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$packageId = 'CP6.Platform.Deployment'
$packageVersion = '0.9.0-alpha.1'
$registrySource = 'https://nuget.pkg.github.com/GTX537/index.json'
$registryPackageUrl = 'https://github.com/users/GTX537/packages/nuget/CP6.Platform.Deployment'
$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))

function Resolve-Cp6P09PublicationPath {
    param([Parameter(Mandatory)][string]$Path)

    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    [IO.Path]::GetFullPath((Join-Path $repositoryRoot $Path))
}

function Assert-Cp6P09PublicationContained {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Candidate
    )

    $rootPath = [IO.Path]::GetFullPath($Root).TrimEnd([IO.Path]::DirectorySeparatorChar)
    $candidatePath = [IO.Path]::GetFullPath($Candidate)
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not $candidatePath.StartsWith($rootPath + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw "p09-publication-path: '$candidatePath' is outside '$rootPath'."
    }
}

function Get-Cp6P09RelativePath {
    param([Parameter(Mandatory)][string]$Path)

    [IO.Path]::GetRelativePath($repositoryRoot, [IO.Path]::GetFullPath($Path)).Replace('\', '/')
}

function Get-Cp6P09RequiredJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$CheckId
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "${CheckId}: required JSON file is missing."
    }
    try {
        Get-Content -LiteralPath $Path -Raw -Encoding UTF8 | ConvertFrom-Json -Depth 100
    }
    catch {
        throw "${CheckId}: required JSON file is invalid."
    }
}

function Assert-Cp6P09Hash {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$CheckId
    )

    if ($Value -cnotmatch '^[0-9a-f]{64}$') {
        throw "${CheckId}: expected a lowercase SHA-256 value."
    }
}

function Assert-Cp6P09Digest {
    param(
        [Parameter(Mandatory)][string]$Value,
        [Parameter(Mandatory)][string]$CheckId
    )

    if ($Value -cnotmatch '^sha256:[0-9a-f]{64}$') {
        throw "${CheckId}: expected an immutable sha256 image digest."
    }
}

if ($SourceGitSha -cnotmatch '^[0-9a-f]{40}$') {
    throw 'p09-publication-source: SourceGitSha must be exactly 40 lowercase hexadecimal characters.'
}
if ($WorkflowRunId -cnotmatch '^[1-9][0-9]*$') {
    throw 'p09-publication-run: WorkflowRunId must be a positive decimal identifier.'
}
if ([string]::IsNullOrWhiteSpace($WorkflowJob) -or $WorkflowJob -cnotmatch '^[a-zA-Z0-9_-]+$') {
    throw 'p09-publication-job: WorkflowJob is invalid.'
}

$resolvedPackageDirectory = Resolve-Cp6P09PublicationPath $PackageDirectory
$resolvedVerificationDirectory = Resolve-Cp6P09PublicationPath $VerificationDirectory
$resolvedP05Result = Resolve-Cp6P09PublicationPath $P05ResultPath
$resolvedP06Result = Resolve-Cp6P09PublicationPath $P06ResultPath
$resolvedRehearsalDirectory = Resolve-Cp6P09PublicationPath $RehearsalDirectory
$resolvedKubernetesDirectory = Resolve-Cp6P09PublicationPath $KubernetesDirectory
$resolvedOutput = Resolve-Cp6P09PublicationPath $OutputPath
Assert-Cp6P09PublicationContained -Root $artifactsRoot -Candidate $resolvedOutput

$expectedOrdinaryName = "$packageId.$packageVersion.nupkg"
$ordinaryPackages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.nupkg' -File |
    Where-Object { -not $_.Name.EndsWith('.snupkg', [StringComparison]::OrdinalIgnoreCase) })
if ($ordinaryPackages.Count -ne 1 -or $ordinaryPackages[0].Name -cne $expectedOrdinaryName) {
    throw "p09-publication-package: expected only '$expectedOrdinaryName'."
}
$ordinaryPackage = $ordinaryPackages[0]
if ($ordinaryPackage.Length -le 0) {
    throw 'p09-publication-package: ordinary package is empty.'
}
$ordinarySha = (Get-FileHash -LiteralPath $ordinaryPackage.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Cp6P09Hash $ordinarySha 'p09-publication-package-hash'

$expectedSymbolName = "$packageId.$packageVersion.snupkg"
$symbolPackages = @(Get-ChildItem -LiteralPath $resolvedPackageDirectory -Filter '*.snupkg' -File)
if ($symbolPackages.Count -gt 1 -or ($symbolPackages.Count -eq 1 -and $symbolPackages[0].Name -cne $expectedSymbolName)) {
    throw "p09-publication-symbols: only '$expectedSymbolName' may be retained."
}
$symbolIdentity = $null
if ($symbolPackages.Count -eq 1) {
    if ($symbolPackages[0].Length -le 0) {
        throw 'p09-publication-symbols: symbol package is empty.'
    }
    $symbolSha = (Get-FileHash -LiteralPath $symbolPackages[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    Assert-Cp6P09Hash $symbolSha 'p09-publication-symbol-hash'
    $symbolIdentity = [ordered]@{
        file = $symbolPackages[0].Name
        sha256 = $symbolSha
        byteLength = $symbolPackages[0].Length
        disposition = 'EvidenceOnly'
    }
}

$requiredGateDirectories = [ordered]@{
    Format = 'format'
    Build = 'build'
    Unit = 'unit'
    P05Real = 'p05-real'
    P06Real = 'p06-real'
    E2E = 'e2e'
    Contract = 'contract'
    Security = 'security'
    P09Real = 'p09real'
}
$gates = [System.Collections.Generic.List[object]]::new()
foreach ($gate in $requiredGateDirectories.GetEnumerator()) {
    $summaryPath = Join-Path (Join-Path $resolvedVerificationDirectory $gate.Value) 'summary.json'
    $summary = Get-Cp6P09RequiredJson -Path $summaryPath -CheckId "p09-publication-gate-$($gate.Key)"
    if ([string]$summary.status -cne 'Passed') {
        throw "p09-publication-gate-$($gate.Key): gate did not pass."
    }
    if ([string]$summary.commitSha -cne $SourceGitSha) {
        throw "p09-publication-gate-$($gate.Key): gate source SHA does not match."
    }
    $gates.Add([ordered]@{
        name = [string]$gate.Key
        path = Get-Cp6P09RelativePath $summaryPath
        status = 'Passed'
    })
}

$p05 = Get-Cp6P09RequiredJson -Path $resolvedP05Result -CheckId 'p09-publication-p05'
$p06 = Get-Cp6P09RequiredJson -Path $resolvedP06Result -CheckId 'p09-publication-p06'
if ([string]$p05.status -cne 'Passed' -or [string]$p06.status -cne 'Passed') {
    throw 'p09-publication-real-profiles: P05 and P06 evidence must both pass.'
}
$gates.Add([ordered]@{ name = 'P05RealEvidence'; path = Get-Cp6P09RelativePath $resolvedP05Result; status = 'Passed' })
$gates.Add([ordered]@{ name = 'P06RealEvidence'; path = Get-Cp6P09RelativePath $resolvedP06Result; status = 'Passed' })

$evidenceFiles = @(Get-ChildItem -LiteralPath $resolvedRehearsalDirectory -Recurse -Filter 'rehearsal-evidence.v1.json' -File)
if ($evidenceFiles.Count -ne 1) {
    throw "p09-publication-evidence: expected one rehearsal Evidence file; found $($evidenceFiles.Count)."
}
$evidencePath = $evidenceFiles[0].FullName
$evidence = Get-Cp6P09RequiredJson -Path $evidencePath -CheckId 'p09-publication-evidence'
if ([string]$evidence.overall -cne 'Passed' -or
    [string]$evidence.platformGitSha -cne $SourceGitSha -or
    [string]$evidence.packageVersion -cne $packageVersion) {
    throw 'p09-publication-evidence: Evidence is not Passed for the exact source and package version.'
}
if ([int]$evidence.teardown.commandExitCode -ne 0 -or
    [int]$evidence.teardown.containerCount -ne 0 -or
    [int]$evidence.teardown.networkCount -ne 0 -or
    [int]$evidence.teardown.volumeCount -ne 0 -or
    [int]$evidence.teardown.imageCount -ne 0 -or
    -not [bool]$evidence.teardown.temporaryDirectoryRemoved) {
    throw 'p09-publication-evidence: Evidence does not prove zero residue.'
}

$profileSha = [string]$evidence.profileSha256
$composeSha = [string]$evidence.composeManifestSha256
$kubernetesSha = [string]$evidence.kubernetesManifestSha256
$evidenceSha = (Get-FileHash -LiteralPath $evidencePath -Algorithm SHA256).Hash.ToLowerInvariant()
Assert-Cp6P09Hash $profileSha 'p09-publication-profile-hash'
Assert-Cp6P09Hash $composeSha 'p09-publication-compose-hash'
Assert-Cp6P09Hash $kubernetesSha 'p09-publication-kubernetes-hash'
Assert-Cp6P09Hash $evidenceSha 'p09-publication-evidence-hash'

$daprDigest = [string]$evidence.runtime.daprImageDigest
$kafkaDigest = [string]$evidence.runtime.kafkaImageDigest
$kubectlDigest = [string]$evidence.runtime.kubectlImageDigest
Assert-Cp6P09Digest $daprDigest 'p09-publication-dapr-image'
Assert-Cp6P09Digest $kafkaDigest 'p09-publication-kafka-image'
Assert-Cp6P09Digest $kubectlDigest 'p09-publication-kubectl-image'

$kubernetesResults = @(Get-ChildItem -LiteralPath $resolvedKubernetesDirectory -Recurse -Filter 'kubernetes-contract-result.v1.json' -File)
$matchingKubernetes = @($kubernetesResults | Where-Object {
    $result = Get-Cp6P09RequiredJson -Path $_.FullName -CheckId 'p09-publication-kubernetes-result'
    [string]$result.status -ceq 'Passed' -and [string]$result.renderedManifestSha256 -ceq $kubernetesSha
})
if ($matchingKubernetes.Count -lt 1) {
    throw 'p09-publication-kubernetes-result: no Passed result matches the rehearsal manifest hash.'
}

$manifest = [ordered]@{
    schemaVersion = 1
    status = 'Candidate'
    source = [ordered]@{
        gitSha = $SourceGitSha
        workflowRunId = $WorkflowRunId
        workflowRunAttempt = $WorkflowRunAttempt
        workflowJob = $WorkflowJob
    }
    package = [ordered]@{
        id = $packageId
        version = $packageVersion
        file = $ordinaryPackage.Name
        sha256 = $ordinarySha
        byteLength = $ordinaryPackage.Length
    }
    symbols = $symbolIdentity
    runtime = [ordered]@{
        profileId = [string]$evidence.profileId
        profileSha256 = $profileSha
        composeManifestSha256 = $composeSha
        kubernetesManifestSha256 = $kubernetesSha
        evidenceSha256 = $evidenceSha
        evidencePath = Get-Cp6P09RelativePath $evidencePath
        daprImageDigest = $daprDigest
        kafkaImageDigest = $kafkaDigest
        kubectlImageDigest = $kubectlDigest
    }
    gates = $gates.ToArray()
    registry = [ordered]@{
        authority = 'GitHub Packages'
        source = $registrySource
        packageUrl = $registryPackageUrl
    }
}

$outputDirectory = Split-Path -Parent $resolvedOutput
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$json = $manifest | ConvertTo-Json -Depth 12 -Compress
if ($json -match '(?i)(authorization|credential|client[_-]?secret|access[_-]?token|private[_-]?key)' -or
    $json.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'p09-publication-manifest-safety: candidate manifest contains forbidden data.'
}
[IO.File]::WriteAllText($resolvedOutput, $json, [Text.UTF8Encoding]::new($false))
$manifest
