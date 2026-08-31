[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$kubectlImage = 'registry.k8s.io/kubectl:v1.34.1'
$offlineHelperImage = 'python:3.13-alpine@sha256:540c7d91f98ff6880174c40e99067bf5941eb54d818a7a5e094d188b196a934d'
$repositoryRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$profilePath = Join-Path $repositoryRoot 'contracts/p09/examples/non-production-runtime-profile.valid.json'
$baseRoot = Join-Path $repositoryRoot 'deploy/p09/kubernetes/base'
$overlayRoot = Join-Path $repositoryRoot 'deploy/p09/kubernetes/overlays/ci'
$artifactRoot = Join-Path $repositoryRoot 'artifacts/p09-kubernetes'
$runId = '{0}-{1}' -f ([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')), ([Guid]::NewGuid().ToString('N').Substring(0, 16))
$runRoot = Join-Path $artifactRoot $runId
$renderOnePath = Join-Path $runRoot 'render-one.yaml'
$renderTwoPath = Join-Path $runRoot 'render-two.yaml'
$kubeconfigPath = Join-Path $runRoot 'unreachable.kubeconfig'
$discoveryCacheRoot = Join-Path $runRoot 'discovery-cache/discovery/127.0.0.1_1'
$kubectlBinaryPath = Join-Path $runRoot 'kubectl'
$certificatePath = Join-Path $runRoot 'offline-loopback.crt'
$privateKeyPath = Join-Path $runRoot 'offline-loopback.key'
$readyPath = Join-Path $runRoot 'offline-loopback.ready'
$violationPath = Join-Path $runRoot 'offline-loopback.violation'
$resultPath = Join-Path $runRoot 'kubernetes-contract-result.v1.json'
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Assert-Cp6P09ContainedPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$Candidate
    )

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar)
    $resolvedCandidate = [System.IO.Path]::GetFullPath($Candidate)
    $prefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
    if (-not $resolvedCandidate.StartsWith($prefix, $comparison)) {
        throw "k8s-artifact-boundary: '$resolvedCandidate' is outside '$resolvedRoot'."
    }
}

function Invoke-Cp6P09Native {
    param(
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = 120
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $FileName
    $startInfo.WorkingDirectory = $repositoryRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    foreach ($argument in $Arguments) {
        [void]$startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    try {
        if (-not $process.Start()) {
            throw "k8s-process-start: failed to start '$FileName'."
        }

        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw "k8s-process-timeout: '$FileName' exceeded $TimeoutSeconds seconds."
        }

        [pscustomobject]@{
            ExitCode = $process.ExitCode
            Stdout = $stdoutTask.GetAwaiter().GetResult()
            Stderr = $stderrTask.GetAwaiter().GetResult()
        }
    }
    finally {
        $process.Dispose()
    }
}

function Invoke-Cp6P09CheckedNative {
    param(
        [Parameter(Mandatory)][string]$CheckId,
        [Parameter(Mandatory)][string]$FileName,
        [Parameter(Mandatory)][string[]]$Arguments,
        [int]$TimeoutSeconds = 120
    )

    $result = Invoke-Cp6P09Native -FileName $FileName -Arguments $Arguments -TimeoutSeconds $TimeoutSeconds
    if ($result.ExitCode -ne 0) {
        $detail = $result.Stderr.Trim()
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $result.Stdout.Trim()
        }

        throw "${CheckId}: command failed with exit code $($result.ExitCode). $detail"
    }

    $result
}

function Get-Cp6P09DotnetCommand {
    if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) {
        return $env:DOTNET_HOST_PATH
    }

    $command = Get-Command dotnet -ErrorAction Stop
    return $command.Source
}

function Test-Cp6P09StrictJsonFile {
    param([Parameter(Mandatory)][string]$Path)

    try {
        $json = [System.IO.File]::ReadAllText($Path)
        $document = [System.Text.Json.JsonDocument]::Parse($json)
        $document.Dispose()
    }
    catch {
        throw "k8s-source-json: invalid JSON in '$([System.IO.Path]::GetFileName($Path))'."
    }
}

function Write-Cp6P09JsonFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object]$Value
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    [System.IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 20 -Compress) + [Environment]::NewLine),
        $utf8NoBom)
}

function Write-Cp6P09OfflineDiscoveryCache {
    $groups = @(
        @{ name = ''; groupVersion = 'v1'; version = 'v1' },
        @{ name = 'apps'; groupVersion = 'apps/v1'; version = 'v1' },
        @{ name = 'batch'; groupVersion = 'batch/v1'; version = 'v1' },
        @{ name = 'networking.k8s.io'; groupVersion = 'networking.k8s.io/v1'; version = 'v1' },
        @{ name = 'dapr.io'; groupVersion = 'dapr.io/v1alpha1'; version = 'v1alpha1' },
        @{ name = 'dapr.io'; groupVersion = 'dapr.io/v2alpha1'; version = 'v2alpha1' }
    )
    $apiGroups = @()
    foreach ($groupName in @('', 'apps', 'batch', 'networking.k8s.io', 'dapr.io')) {
        $versions = @($groups | Where-Object name -eq $groupName | ForEach-Object {
            [ordered]@{ groupVersion = $_.groupVersion; version = $_.version }
        })
        $apiGroups += [ordered]@{
            name = $groupName
            versions = $versions
            preferredVersion = $versions[0]
        }
    }
    Write-Cp6P09JsonFile -Path (Join-Path $discoveryCacheRoot 'servergroups.json') -Value ([ordered]@{
        kind = 'APIGroupList'
        apiVersion = 'v1'
        groups = $apiGroups
    })

    $resourceLists = [ordered]@{
        'v1' = @(
            @{ name = 'namespaces'; kind = 'Namespace'; namespaced = $false },
            @{ name = 'serviceaccounts'; kind = 'ServiceAccount'; namespaced = $true },
            @{ name = 'configmaps'; kind = 'ConfigMap'; namespaced = $true },
            @{ name = 'services'; kind = 'Service'; namespaced = $true }
        )
        'apps/v1' = @(
            @{ name = 'deployments'; kind = 'Deployment'; namespaced = $true }
        )
        'batch/v1' = @(
            @{ name = 'jobs'; kind = 'Job'; namespaced = $true }
        )
        'networking.k8s.io/v1' = @(
            @{ name = 'networkpolicies'; kind = 'NetworkPolicy'; namespaced = $true }
        )
        'dapr.io/v1alpha1' = @(
            @{ name = 'components'; kind = 'Component'; namespaced = $true }
        )
        'dapr.io/v2alpha1' = @(
            @{ name = 'subscriptions'; kind = 'Subscription'; namespaced = $true }
        )
    }
    foreach ($entry in $resourceLists.GetEnumerator()) {
        $resources = @($entry.Value | ForEach-Object {
            [ordered]@{
                name = $_.name
                singularName = ''
                namespaced = $_.namespaced
                kind = $_.kind
                verbs = @('create', 'get', 'patch')
            }
        })
        Write-Cp6P09JsonFile `
            -Path (Join-Path $discoveryCacheRoot "$($entry.Key)/serverresources.json") `
            -Value ([ordered]@{
                kind = 'APIResourceList'
                apiVersion = 'v1'
                groupVersion = $entry.Key
                resources = $resources
            })
    }
}

function Write-Cp6P09LoopbackCertificate {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=127.0.0.1',
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $subjectAlternativeName = [System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
        $subjectAlternativeName.AddIpAddress([System.Net.IPAddress]::Loopback)
        $request.CertificateExtensions.Add($subjectAlternativeName.Build())
        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddMinutes(-1),
            [DateTimeOffset]::UtcNow.AddHours(1))
        try {
            [System.IO.File]::WriteAllText($certificatePath, $certificate.ExportCertificatePem(), $utf8NoBom)
            [System.IO.File]::WriteAllText($privateKeyPath, $rsa.ExportPkcs8PrivateKeyPem(), $utf8NoBom)
        }
        finally {
            $certificate.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }
}

if (-not (Test-Path -LiteralPath $profilePath -PathType Leaf)) {
    throw 'k8s-profile: frozen P09 Profile is missing.'
}
if (-not (Test-Path -LiteralPath (Join-Path $baseRoot 'kustomization.yaml') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $overlayRoot 'kustomization.yaml') -PathType Leaf)) {
    throw 'k8s-kustomization: base or CI overlay is missing.'
}

$resourceFiles = @(Get-ChildItem -LiteralPath $baseRoot -Filter '*.json' -File | Sort-Object Name)
$patchFiles = @(Get-ChildItem -LiteralPath $overlayRoot -Filter '*.json' -File | Sort-Object Name)
if ($resourceFiles.Count -eq 0) {
    throw 'k8s-object-set: no source resource JSON files were found.'
}

Test-Cp6P09StrictJsonFile -Path $profilePath
foreach ($sourceFile in @($resourceFiles) + @($patchFiles)) {
    Test-Cp6P09StrictJsonFile -Path $sourceFile.FullName
}

$dotnetCommand = Get-Cp6P09DotnetCommand
$strictJsonArguments = @(
    'run',
    '--project',
    'tests/CP6.Platform.DeploymentTests/P09Validator/P09Validator.csproj',
    '--configuration',
    'Release',
    '--',
    '--strict-json'
) + @($profilePath) + @($resourceFiles.FullName) + @($patchFiles.FullName)
[void](Invoke-Cp6P09CheckedNative `
    -CheckId 'k8s-source-json' `
    -FileName $dotnetCommand `
    -Arguments $strictJsonArguments `
    -TimeoutSeconds 180)

$validatorArguments = @(
    'run',
    '--project',
    'tests/CP6.Platform.DeploymentTests/P09Validator/P09Validator.csproj',
    '--configuration',
    'Release',
    '--no-build',
    '--',
    '--kubernetes',
    $profilePath
) + @($resourceFiles.FullName)
$validatorResult = Invoke-Cp6P09CheckedNative `
    -CheckId 'k8s-cross-object-policy' `
    -FileName $dotnetCommand `
    -Arguments $validatorArguments `
    -TimeoutSeconds 180
try {
    $validatorOutput = $validatorResult.Stdout | ConvertFrom-Json -Depth 10
}
catch {
    throw 'k8s-cross-object-policy: validator output was not JSON.'
}
if ($validatorOutput.status -ne 'Valid') {
    throw 'k8s-cross-object-policy: validator did not return Valid.'
}

New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
Assert-Cp6P09ContainedPath -Root $artifactRoot -Candidate $runRoot
Write-Cp6P09OfflineDiscoveryCache
$extractContainerName = "cp6-p09-kubectl-$($runId.ToLowerInvariant().Replace('-', ''))"
$extractContainerCreated = $false

try {
    $sourceMount = "${repositoryRoot}:/workspace:ro"
    $artifactMount = "${runRoot}:/artifacts"
    $dockerPrefix = @('run', '--rm', '--network', 'none', '-v', $sourceMount, '-v', $artifactMount, $kubectlImage)

    $renderArguments = $dockerPrefix + @(
        'kustomize',
        '/workspace/deploy/p09/kubernetes/overlays/ci'
    )
    $renderOne = Invoke-Cp6P09CheckedNative -CheckId 'k8s-render-one' -FileName 'docker' -Arguments $renderArguments
    $renderTwo = Invoke-Cp6P09CheckedNative -CheckId 'k8s-render-two' -FileName 'docker' -Arguments $renderArguments
    [System.IO.File]::WriteAllText($renderOnePath, $renderOne.Stdout, $utf8NoBom)
    [System.IO.File]::WriteAllText($renderTwoPath, $renderTwo.Stdout, $utf8NoBom)

    $renderOneSha = (Get-FileHash -LiteralPath $renderOnePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $renderTwoSha = (Get-FileHash -LiteralPath $renderTwoPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($renderOneSha -ne $renderTwoSha) {
        throw 'k8s-render-deterministic: two offline Kustomize renders differ.'
    }

    $kubeconfig = @'
apiVersion: v1
kind: Config
clusters:
  - name: unreachable
    cluster:
      server: https://127.0.0.1:1
      insecure-skip-tls-verify: true
contexts:
  - name: offline
    context:
      cluster: unreachable
      user: offline
current-context: offline
users:
  - name: offline
    user:
      token: offline-noncredential
'@
    [System.IO.File]::WriteAllText($kubeconfigPath, $kubeconfig, $utf8NoBom)

    [void](Invoke-Cp6P09CheckedNative `
        -CheckId 'k8s-kubectl-extract-create' `
        -FileName 'docker' `
        -Arguments @('create', '--name', $extractContainerName, $kubectlImage, 'version', '--client=true'))
    $extractContainerCreated = $true
    [void](Invoke-Cp6P09CheckedNative `
        -CheckId 'k8s-kubectl-extract-copy' `
        -FileName 'docker' `
        -Arguments @('cp', "${extractContainerName}:/bin/kubectl", $kubectlBinaryPath))
    [void](Invoke-Cp6P09CheckedNative `
        -CheckId 'k8s-kubectl-extract-remove' `
        -FileName 'docker' `
        -Arguments @('rm', $extractContainerName))
    $extractContainerCreated = $false
    Write-Cp6P09LoopbackCertificate

    $offlineApply = @'
python /workspace/tests/p09/offline-kube-api.py --cert /artifacts/offline-loopback.crt --key /artifacts/offline-loopback.key --ready /artifacts/offline-loopback.ready --violation /artifacts/offline-loopback.violation &
sentinel_pid=$!
attempt=0
while [ ! -f /artifacts/offline-loopback.ready ] && [ "$attempt" -lt 50 ]; do
  attempt=$((attempt + 1))
  sleep 0.1
done
if [ ! -f /artifacts/offline-loopback.ready ]; then
  kill "$sentinel_pid" 2>/dev/null || true
  exit 70
fi
/artifacts/kubectl "$@"
status=$?
kill "$sentinel_pid" 2>/dev/null || true
wait "$sentinel_pid" 2>/dev/null || true
exit "$status"
'@
    $applyArguments = @(
        'run', '--rm', '--network', 'none',
        '-v', $sourceMount,
        '-v', $artifactMount,
        '--entrypoint', 'sh',
        $offlineHelperImage,
        '-c', $offlineApply,
        'cp6-offline-apply',
        '--kubeconfig=/artifacts/unreachable.kubeconfig',
        '--cache-dir=/artifacts/discovery-cache',
        'apply', '--dry-run=client', '--validate=false',
        '-f', '/artifacts/render-one.yaml'
    )
    [void](Invoke-Cp6P09CheckedNative -CheckId 'k8s-client-dry-run' -FileName 'docker' -Arguments $applyArguments)
    if (Test-Path -LiteralPath $violationPath -PathType Leaf) {
        throw 'k8s-client-dry-run-write: kubectl attempted a mutating API request.'
    }

    $versionArguments = $dockerPrefix + @('version', '--client=true', '--output=json')
    $versionResult = Invoke-Cp6P09CheckedNative -CheckId 'k8s-kubectl-version' -FileName 'docker' -Arguments $versionArguments
    try {
        $kubectlVersion = ($versionResult.Stdout | ConvertFrom-Json -Depth 10).clientVersion.gitVersion
    }
    catch {
        throw 'k8s-kubectl-version: kubectl version output was not JSON.'
    }

    $evidence = [ordered]@{
        schemaVersion = '1'
        status = 'Passed'
        renderedManifestSha256 = $renderOneSha
        kubectlImage = $kubectlImage
        kubectlVersion = $kubectlVersion
        checks = @(
            [ordered]@{ id = 'k8s-source-json'; status = 'Passed' },
            [ordered]@{ id = 'k8s-cross-object-policy'; status = 'Passed' },
            [ordered]@{ id = 'k8s-render-deterministic'; status = 'Passed' },
            [ordered]@{ id = 'k8s-network-none'; status = 'Passed' },
            [ordered]@{ id = 'k8s-client-dry-run'; status = 'Passed' },
            [ordered]@{ id = 'k8s-no-api-write'; status = 'Passed' }
        )
    }
    [System.IO.File]::WriteAllText(
        $resultPath,
        (($evidence | ConvertTo-Json -Depth 10) + [Environment]::NewLine),
        $utf8NoBom)

    [pscustomobject]@{
        Status = 'Passed'
        RunId = $runId
        ManifestSha256 = $renderOneSha
        ArtifactsDirectory = [System.IO.Path]::GetRelativePath($repositoryRoot, $runRoot).Replace('\', '/')
    } | ConvertTo-Json -Compress
}
finally {
    if ($extractContainerCreated) {
        [void](Invoke-Cp6P09Native `
            -FileName 'docker' `
            -Arguments @('rm', '--force', $extractContainerName) `
            -TimeoutSeconds 30)
    }
    foreach ($temporaryPath in @(
        $renderOnePath,
        $renderTwoPath,
        $kubeconfigPath,
        $kubectlBinaryPath,
        $certificatePath,
        $privateKeyPath,
        $readyPath,
        $violationPath
    )) {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Assert-Cp6P09ContainedPath -Root $runRoot -Candidate $temporaryPath
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
    if (Test-Path -LiteralPath $discoveryCacheRoot -PathType Container) {
        Assert-Cp6P09ContainedPath -Root $runRoot -Candidate $discoveryCacheRoot
        [System.IO.Directory]::Delete($discoveryCacheRoot, $true)
        $discoveryParent = Split-Path -Parent $discoveryCacheRoot
        if ((Test-Path -LiteralPath $discoveryParent -PathType Container) -and
            @(Get-ChildItem -LiteralPath $discoveryParent -Force).Count -eq 0) {
            [System.IO.Directory]::Delete($discoveryParent)
        }
        $cacheParent = Split-Path -Parent $discoveryParent
        if ((Test-Path -LiteralPath $cacheParent -PathType Container) -and
            @(Get-ChildItem -LiteralPath $cacheParent -Force).Count -eq 0) {
            [System.IO.Directory]::Delete($cacheParent)
        }
    }
}
