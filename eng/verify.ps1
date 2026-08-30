[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Format', 'Build', 'Unit', 'Integration', 'Contract', 'Security', 'E2E', 'Performance', 'Migration')]
    [string]$Gate,

    [string]$Profile = 'local'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repositoryRoot 'CP6.Platform.sln'
$gateName = $Gate.ToLowerInvariant()
$outputRoot = Join-Path $repositoryRoot "artifacts/verify/$gateName"
$summaryPath = Join-Path $outputRoot 'summary.json'
$junitPath = Join-Path $outputRoot 'results.junit.xml'
$startedAt = [DateTimeOffset]::UtcNow
$status = 'Passed'
$failureMessage = $null
$checks = [System.Collections.Generic.List[object]]::new()
$packageVersion = '0.8.0-alpha.2'
$runtimePackageProjects = @(
    'src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj',
    'src/CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj',
    'src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj',
    'src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj',
    'src/CP6.Platform.EntityFramework/CP6.Platform.EntityFramework.csproj'
)

if (-not $outputRoot.StartsWith((Join-Path $repositoryRoot 'artifacts'), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to write verification output outside the repository artifacts directory: $outputRoot"
}

if (Test-Path -LiteralPath $outputRoot) {
    Remove-Item -LiteralPath $outputRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

function Invoke-DotNetStep {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name,

        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    $stepStarted = [DateTimeOffset]::UtcNow
    $logPath = Join-Path $outputRoot "$($Name.ToLowerInvariant()).log"
    & dotnet @Arguments 2>&1 | Tee-Object -FilePath $logPath
    $exitCode = $LASTEXITCODE
    $checks.Add([ordered]@{
        name = $Name
        status = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
        durationMs = [Math]::Round(([DateTimeOffset]::UtcNow - $stepStarted).TotalMilliseconds)
        log = [IO.Path]::GetRelativePath($repositoryRoot, $logPath).Replace('\', '/')
    })

    if ($exitCode -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $exitCode."
    }
}

function Add-NotApplicableCheck {
    param([Parameter(Mandatory = $true)][string]$Reason)

    $script:status = 'NotApplicable'
    $checks.Add([ordered]@{
        name = $Gate
        status = 'NotApplicable'
        durationMs = 0
        reason = $Reason
    })
}

function Assert-ReproduciblePackages {
    $firstDirectory = Join-Path $outputRoot 'pack-first'
    $secondDirectory = Join-Path $outputRoot 'pack-second'
    New-Item -ItemType Directory -Path $firstDirectory, $secondDirectory -Force | Out-Null

    foreach ($project in $runtimePackageProjects) {
        $name = [IO.Path]::GetFileNameWithoutExtension($project)
        Invoke-DotNetStep -Name "PackFirst-$name" -Arguments @(
            'pack', $project, '--configuration', 'Release', '--no-build',
            '--output', $firstDirectory, "-p:Version=$packageVersion"
        )
        Invoke-DotNetStep -Name "PackSecond-$name" -Arguments @(
            'pack', $project, '--configuration', 'Release', '--no-build',
            '--output', $secondDirectory, "-p:Version=$packageVersion"
        )
    }

    function Get-PackageContentManifest {
        param([Parameter(Mandatory = $true)][string]$Directory)

        return @(Get-ChildItem -LiteralPath $Directory -File | Sort-Object Name | ForEach-Object {
            $package = $_
            $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
            try {
                # NuGet assigns random OPC relationship/core-property identifiers on every pack.
                # Compare the signed/consumed payload and nuspec, not this container bookkeeping.
                $entries = @($archive.Entries |
                    Where-Object {
                        $_.FullName -ne '_rels/.rels' -and
                        $_.FullName -notlike 'package/services/metadata/core-properties/*'
                    } |
                    Sort-Object FullName |
                    ForEach-Object {
                        $stream = $_.Open()
                        $algorithm = [Security.Cryptography.SHA256]::Create()
                        try {
                            $hash = [Convert]::ToHexString($algorithm.ComputeHash($stream))
                        } finally {
                            $algorithm.Dispose()
                            $stream.Dispose()
                        }

                        [ordered]@{ Name = $_.FullName; Length = $_.Length; Hash = $hash }
                    })
            } finally {
                $archive.Dispose()
            }

            [ordered]@{ Name = $package.Name; Entries = $entries }
        })
    }

    $firstPackages = Get-PackageContentManifest -Directory $firstDirectory
    $secondPackages = Get-PackageContentManifest -Directory $secondDirectory

    $expectedPackageIds = @('CP6.Platform.Abstractions', 'CP6.Platform.AspNetCore', 'CP6.Platform.Contracts', 'CP6.Platform.EntityFramework', 'CP6.Platform.Messaging')
    $expectedNames = @($expectedPackageIds | ForEach-Object {
        "$($_).$packageVersion.nupkg"
        "$($_).$packageVersion.snupkg"
    } | Sort-Object)
    $actualNames = @($firstPackages.Name | Sort-Object)
    if (($expectedNames | ConvertTo-Json -Compress) -ne ($actualNames | ConvertTo-Json -Compress)) {
        throw "Package set differs from the five approved P08-S01 package IDs: $($actualNames -join ', ')."
    }
    if ($actualNames -match 'CP6\.Platform\.Testing') {
        throw 'CP6.Platform.Testing is repository-only and must not be packaged.'
    }

    foreach ($packageId in $expectedPackageIds) {
        $runtimePackage = $firstPackages | Where-Object { $_.Name -eq "$packageId.$packageVersion.nupkg" }
        $assemblyPath = "lib/net8.0/$packageId.dll"
        if (-not ($runtimePackage.Entries | Where-Object { $_.Name -eq $assemblyPath -and $_.Length -gt 0 })) {
            throw "$($runtimePackage.Name) does not contain the expected non-empty $assemblyPath runtime assembly."
        }
    }

    $messagingPackage = $firstPackages | Where-Object { $_.Name -eq "CP6.Platform.Messaging.$packageVersion.nupkg" }
    $requiredContractEntries = @(
        'contracts/contract-bundle.v1.json',
        'contracts/events/platform/contract-example-changed/v1/schema.json',
        'contracts/events/platform/contract-example-changed/v1/examples/valid.json',
        'contracts/events/platform/contract-example-changed/v1/examples/missing-required.json',
        'contracts/events/platform/contract-example-changed/v1/examples/unknown-optional.json',
        'contracts/events/platform/contract-example-changed/v1/examples/wrong-type.json',
        'contracts/events/platform/contract-example-changed/v1/examples/pii-negative.json'
    )
    $messagingEntries = @($messagingPackage.Entries.Name)
    foreach ($requiredEntry in $requiredContractEntries) {
        if ($requiredEntry -notin $messagingEntries) {
            throw "Messaging package is missing required P04 contract asset: $requiredEntry"
        }
    }
    $checks.Add([ordered]@{
        name = 'ContractBundlePackageContent'
        status = 'Passed'
        durationMs = 0
        assetCount = $requiredContractEntries.Count
    })

    $contractsPackage = $firstPackages | Where-Object { $_.Name -eq "CP6.Platform.Contracts.$packageVersion.nupkg" }
    $requiredSloEntries = @(
        'contracts/observability/slo-evidence/v1/assets.v1.json',
        'contracts/observability/slo-evidence/v1/schema.json',
        'contracts/observability/slo-evidence/v1/examples/non-candidate-indeterminate.json',
        'contracts/observability/slo-evidence/v1/examples/partial-indeterminate.json',
        'contracts/observability/slo-evidence/v1/examples/pii-negative.json',
        'contracts/observability/slo-evidence/v1/examples/valid-pass.json'
    )
    $contractsEntries = @($contractsPackage.Entries.Name)
    foreach ($requiredEntry in $requiredSloEntries) {
        if ($requiredEntry -notin $contractsEntries) {
            throw "Contracts package is missing required P08 SLO evidence asset: $requiredEntry"
        }
    }

    $runtimePackages = @($firstPackages | Where-Object { $_.Name.EndsWith('.nupkg', [StringComparison]::Ordinal) -and -not $_.Name.EndsWith('.snupkg', [StringComparison]::Ordinal) })
    foreach ($package in $runtimePackages) {
        if ($package.Name -ne $contractsPackage.Name -and
            ($package.Entries.Name | Where-Object { $_.StartsWith('contracts/observability/', [StringComparison]::Ordinal) })) {
            throw "$($package.Name) contains SLO evidence assets owned only by CP6.Platform.Contracts."
        }

        if ($package.Name -ne $messagingPackage.Name -and
            ($package.Entries.Name | Where-Object {
                $_ -eq 'contracts/contract-bundle.v1.json' -or
                $_.StartsWith('contracts/events/', [StringComparison]::Ordinal)
            })) {
            throw "$($package.Name) contains P04 event assets owned only by CP6.Platform.Messaging."
        }
    }
    $checks.Add([ordered]@{
        name = 'SloEvidencePackageContent'
        status = 'Passed'
        durationMs = 0
        assetCount = $requiredSloEntries.Count
    })

    $textExtensions = @('.cs', '.json', '.md', '.nuspec', '.props', '.targets', '.txt', '.xml')
    foreach ($packageFile in Get-ChildItem -LiteralPath $firstDirectory -File) {
        $archive = [IO.Compression.ZipFile]::OpenRead($packageFile.FullName)
        try {
            foreach ($entry in $archive.Entries) {
                if ($entry.FullName -match '(^|/)(tests?|CP6\.Platform\.Testing)(/|$)' -or
                    $entry.FullName -match '\.Tests(?:\.|/)') {
                    throw "$($packageFile.Name) contains a test namespace or asset: $($entry.FullName)"
                }

                if ($entry.FullName -match '^[A-Za-z]:[\\/]' -or
                    $entry.FullName -match '^/(home|Users)/') {
                    throw "$($packageFile.Name) contains a machine-specific entry path: $($entry.FullName)"
                }

                if ($textExtensions -contains [IO.Path]::GetExtension($entry.FullName)) {
                    $stream = $entry.Open()
                    $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true)
                    try {
                        $text = $reader.ReadToEnd()
                    } finally {
                        $reader.Dispose()
                        $stream.Dispose()
                    }

                    if ($text -match '(?i)[A-Z]:[\\/]Users[\\/]' -or
                        $text -match '(?i)/(home|Users)/[^/\s]+/' -or
                        $text.Contains($repositoryRoot, [StringComparison]::OrdinalIgnoreCase)) {
                        throw "$($packageFile.Name) contains machine-specific content in $($entry.FullName)."
                    }
                }
            }
        } finally {
            $archive.Dispose()
        }
    }
    $checks.Add([ordered]@{
        name = 'PackageContentSafety'
        status = 'Passed'
        durationMs = 0
        packageCount = $firstPackages.Count
    })

    if (($firstPackages | ConvertTo-Json -Depth 8 -Compress) -ne ($secondPackages | ConvertTo-Json -Depth 8 -Compress)) {
        throw 'Package content is not reproducible: the two entry-level SHA-256 manifests differ.'
    }

    $manifestPath = Join-Path $outputRoot 'package-hashes.json'
    @($firstPackages) | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $checks.Add([ordered]@{
        name = 'PackageReproducibility'
        status = 'Passed'
        durationMs = 0
        packageCount = $firstPackages.Count
        manifest = [IO.Path]::GetRelativePath($repositoryRoot, $manifestPath).Replace('\', '/')
    })
}

function Write-Evidence {
    $completedAt = [DateTimeOffset]::UtcNow
    $commitSha = (& git -C $repositoryRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0) {
        $commitSha = $null
    } else {
        $commitSha = $commitSha.Trim()
    }

    $summary = [ordered]@{
        schemaVersion = 1
        gate = $Gate
        profile = $Profile
        status = $status
        startedAtUtc = $startedAt.ToString('o')
        completedAtUtc = $completedAt.ToString('o')
        durationMs = [Math]::Round(($completedAt - $startedAt).TotalMilliseconds)
        commitSha = $commitSha
        checks = @($checks)
    }

    if ($failureMessage) {
        $summary.error = $failureMessage
    }

    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $summaryPath -Encoding utf8

    $escapedGate = [Security.SecurityElement]::Escape($Gate)
    $durationSeconds = [Math]::Round(($completedAt - $startedAt).TotalSeconds, 3).ToString([Globalization.CultureInfo]::InvariantCulture)
    if ($status -eq 'Failed') {
        $escapedFailure = [Security.SecurityElement]::Escape($failureMessage)
        $resultElement = "<failure message=`"$escapedFailure`" />"
        $failures = 1
        $skipped = 0
    } elseif ($status -eq 'NotApplicable') {
        $reason = [string]$checks[0].reason
        $escapedReason = [Security.SecurityElement]::Escape($reason)
        $resultElement = "<skipped message=`"$escapedReason`" />"
        $failures = 0
        $skipped = 1
    } else {
        $resultElement = ''
        $failures = 0
        $skipped = 0
    }

    $junit = @"
<?xml version="1.0" encoding="utf-8"?>
<testsuite name="CP6.Platform.$escapedGate" tests="1" failures="$failures" skipped="$skipped" time="$durationSeconds">
  <testcase classname="CP6.Platform.Verify" name="$escapedGate" time="$durationSeconds">$resultElement</testcase>
</testsuite>
"@
    Set-Content -LiteralPath $junitPath -Value $junit -Encoding utf8
}

Push-Location $repositoryRoot
try {
    switch ($Gate) {
        'Format' {
            Invoke-DotNetStep -Name 'Format' -Arguments @('format', $solutionPath, '--verify-no-changes')
        }
        'Build' {
            Invoke-DotNetStep -Name 'Restore' -Arguments @('restore', $solutionPath)
            Invoke-DotNetStep -Name 'Build' -Arguments @('build', $solutionPath, '--configuration', 'Release', '--no-restore')
        }
        'Unit' {
            Invoke-DotNetStep -Name 'Unit' -Arguments @(
                'test', 'tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj',
                '--configuration', 'Release'
            )
            & pwsh (Join-Path $PSScriptRoot 'test-verify-failure.ps1')
            if ($LASTEXITCODE -ne 0) {
                throw "Verification failure-contract self-test failed with exit code $LASTEXITCODE."
            }
            $checks.Add([ordered]@{
                name = 'FailureContract'
                status = 'Passed'
                durationMs = 0
            })
        }
        'Contract' {
            Invoke-DotNetStep -Name 'Restore' -Arguments @('restore', $solutionPath)
            Invoke-DotNetStep -Name 'Build' -Arguments @('build', $solutionPath, '--configuration', 'Release', '--no-restore')
            Invoke-DotNetStep -Name 'Architecture' -Arguments @(
                'test', 'tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj',
                '--configuration', 'Release', '--no-build'
            )
            Assert-ReproduciblePackages
        }
        'Security' {
            Invoke-DotNetStep -Name 'Restore' -Arguments @('restore', $solutionPath, '--force-evaluate')
            Invoke-DotNetStep -Name 'VulnerablePackages' -Arguments @(
                'list', $solutionPath, 'package', '--vulnerable', '--include-transitive',
                '--source', 'https://api.nuget.org/v3/index.json'
            )
        }
        'Integration' {
            Invoke-DotNetStep -Name 'AspNetCoreIntegration' -Arguments @(
                'test', 'tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj',
                '--configuration', 'Release'
            )
            if ($Profile -eq 'p05-real') {
                $stepStarted = [DateTimeOffset]::UtcNow
                $logPath = Join-Path $outputRoot 'dapr-kafka.log'
                & pwsh (Join-Path $PSScriptRoot 'run-p05-integration.ps1') 2>&1 | Tee-Object -FilePath $logPath
                $exitCode = $LASTEXITCODE
                $checks.Add([ordered]@{
                    name = 'DaprKafkaIntegration'
                    status = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
                    durationMs = [Math]::Round(([DateTimeOffset]::UtcNow - $stepStarted).TotalMilliseconds)
                    log = [IO.Path]::GetRelativePath($repositoryRoot, $logPath).Replace('\', '/')
                })
                if ($exitCode -ne 0) {
                    throw "Real Dapr/Kafka integration failed with exit code $exitCode."
                }
            }
            if ($Profile -eq 'p06-real') {
                $stepStarted = [DateTimeOffset]::UtcNow
                $logPath = Join-Path $outputRoot 'sql-server.log'
                & pwsh (Join-Path $PSScriptRoot 'run-p06-sql-integration.ps1') 2>&1 | Tee-Object -FilePath $logPath
                $exitCode = $LASTEXITCODE
                $checks.Add([ordered]@{
                    name = 'SqlServerTransactionalMessagingIntegration'
                    status = if ($exitCode -eq 0) { 'Passed' } else { 'Failed' }
                    durationMs = [Math]::Round(([DateTimeOffset]::UtcNow - $stepStarted).TotalMilliseconds)
                    log = [IO.Path]::GetRelativePath($repositoryRoot, $logPath).Replace('\', '/')
                })
                if ($exitCode -ne 0) {
                    throw "Real SQL Server transactional messaging integration failed with exit code $exitCode."
                }
            }
        }
        'E2E' {
            Invoke-DotNetStep -Name 'PlatformE2E' -Arguments @(
                'test', 'tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj',
                '--configuration', 'Release',
                '--filter', 'FullyQualifiedName~GatewayContractTests|FullyQualifiedName~ObservabilityEndToEndTests'
            )
        }
        'Performance' { Add-NotApplicableCheck 'P08-S01 freezes evidence contracts but does not claim production performance or SLO thresholds.' }
        'Migration' { Add-NotApplicableCheck 'P08-S01 contains no database schema or migration assets.' }
    }
} catch {
    $status = 'Failed'
    $failureMessage = $_.Exception.Message
    if (-not $checks.Exists({ param($check) $check.status -eq 'Failed' })) {
        $checks.Add([ordered]@{
            name = $Gate
            status = 'Failed'
            durationMs = 0
            reason = $failureMessage
        })
    }
} finally {
    Pop-Location
    Write-Evidence
}

Write-Host "[$Gate] $status - $summaryPath"
if ($status -eq 'Failed') {
    Write-Error $failureMessage
    exit 1
}
