[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$runnerPath = Join-Path $repositoryRoot 'eng/test-p09-kubernetes.ps1'
$offlineApiPath = Join-Path $repositoryRoot 'tests/p09/offline-kube-api.py'
$baseRoot = Join-Path $repositoryRoot 'deploy/p09/kubernetes/base'
$overlayRoot = Join-Path $repositoryRoot 'deploy/p09/kubernetes/overlays/ci'

$failures = [System.Collections.Generic.List[string]]::new()

if (-not (Test-Path -LiteralPath $runnerPath -PathType Leaf)) {
    $failures.Add('k8s-runner-missing')
}

if (-not (Test-Path -LiteralPath (Join-Path $baseRoot 'kustomization.yaml') -PathType Leaf)) {
    $failures.Add('k8s-base-missing')
}

if (-not (Test-Path -LiteralPath (Join-Path $overlayRoot 'kustomization.yaml') -PathType Leaf)) {
    $failures.Add('k8s-overlay-missing')
}

if (Test-Path -LiteralPath $runnerPath -PathType Leaf) {
    $runner = Get-Content -LiteralPath $runnerPath -Raw
    foreach ($contract in @(
        @{ Id = 'k8s-network-isolation'; Pattern = '--network.+none' },
        @{ Id = 'k8s-fixed-kubectl'; Pattern = 'registry\.k8s\.io/kubectl:v1\.34\.1' },
        @{ Id = 'k8s-client-dry-run'; Pattern = '--dry-run=client' },
        @{ Id = 'k8s-offline-validation'; Pattern = '--validate=false' },
        @{ Id = 'k8s-unreachable-server'; Pattern = 'https://127\.0\.0\.1:1' },
        @{ Id = 'k8s-read-only-source'; Pattern = ':/workspace:ro' }
    )) {
        if ($runner -notmatch $contract.Pattern) {
            $failures.Add($contract.Id)
        }
    }

    $unsafeApply = [regex]::Matches($runner, '(?im)^.*kubectl.*\bapply\b.*$') |
        Where-Object { $_.Value -notmatch '--dry-run=client' }
    if ($unsafeApply.Count -gt 0) {
        $failures.Add('k8s-unsafe-apply')
    }

    if ($runner -match '(?i)\b(kubeconfig|context)\s*=.*\bparam\b') {
        $failures.Add('k8s-caller-cluster-context')
    }
    if ($runner -match '\$env:KUBECONFIG|--context') {
        $failures.Add('k8s-inherited-cluster-context')
    }
    if ([regex]::Matches($runner, "'apply'").Count -ne 1) {
        $failures.Add('k8s-apply-count')
    }
}

if (-not (Test-Path -LiteralPath $offlineApiPath -PathType Leaf)) {
    $failures.Add('k8s-loopback-sentinel-missing')
}
else {
    $offlineApi = Get-Content -LiteralPath $offlineApiPath -Raw
    foreach ($method in @('POST', 'PUT', 'PATCH', 'DELETE')) {
        if ($offlineApi -notmatch "do_${method}\s*=\s*_reject_mutation") {
            $failures.Add("k8s-loopback-${method}-not-rejected".ToLowerInvariant())
        }
    }
    if ($offlineApi -notmatch 'HTTPServer\(\("127\.0\.0\.1",\s*1\)') {
        $failures.Add('k8s-loopback-sentinel-boundary')
    }
}

if ($failures.Count -gt 0) {
    throw "P09 Kubernetes negative contracts failed: $($failures -join ', ')"
}

Write-Host 'P09 Kubernetes negative contracts passed.'
