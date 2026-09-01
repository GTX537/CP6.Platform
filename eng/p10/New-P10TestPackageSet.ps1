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

    [string]$OutputPath = 'artifacts/p10-test/packages',

    [switch]$InjectFailureAfterSigning
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'P10 test package-set creation requires Windows.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedOutput = [IO.Path]::GetFullPath($OutputPath, $repositoryRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedOutput.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package-set output must be below $artifactsRoot."
}
if ((Test-Path -LiteralPath $resolvedOutput) -and @(Get-ChildItem -LiteralPath $resolvedOutput -Force).Count -ne 0) {
    throw 'Package-set output directory must be empty.'
}
[IO.Directory]::CreateDirectory($resolvedOutput) | Out-Null

$privateRoot = Join-Path $artifactsRoot ("private\" + [Guid]::NewGuid().ToString('N'))
$privatePackages = Join-Path $privateRoot 'packages'
$privateResults = Join-Path $privateRoot 'test-results'
$privateRaw = Join-Path $privateRoot 'raw'
[IO.Directory]::CreateDirectory($privatePackages) | Out-Null
[IO.Directory]::CreateDirectory($privateResults) | Out-Null
[IO.Directory]::CreateDirectory($privateRaw) | Out-Null

$packageIds = @(
    'CP6.Platform.Abstractions',
    'CP6.Platform.AspNetCore',
    'CP6.Platform.Contracts',
    'CP6.Platform.Deployment',
    'CP6.Platform.EntityFramework',
    'CP6.Platform.Messaging',
    'CP6.Platform.Release'
)
$packageVersion = "0.10.0-test.$($SourceGitSha.Substring(0, 12)).$RunAttempt"
$invocationId = "p10-s02:$SourceGitSha`:$RunId`:$RunAttempt"
$certificateState = $null

function Format-UtcMilliseconds([DateTimeOffset]$Value) {
    return $Value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
}

function Invoke-Checked([string]$FileName, [string[]]$Arguments) {
    & $FileName @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName failed with exit code $LASTEXITCODE."
    }
}

function Write-RawJson([string]$Path, $Value) {
    $json = $Value | ConvertTo-Json -Depth 30 -Compress
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Invoke-ReleaseTool([string[]]$Arguments) {
    $toolPath = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\bin\Release\net8.0\CP6.Platform.ReleaseTool.dll'
    if (-not (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        throw 'The Release tool was not produced by the one solution build.'
    }
    Invoke-Checked 'dotnet' (@($toolPath) + $Arguments)
}

function Invoke-Gate([string]$Name, [string]$Project, [string[]]$AdditionalArguments) {
    $resultDirectory = Join-Path $privateResults $Name
    [IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
    $trxName = "$Name.trx"
    $started = [DateTimeOffset]::UtcNow
    $arguments = @(
        'test', $Project,
        '--configuration', 'Release',
        '--no-build', '--no-restore',
        '--results-directory', $resultDirectory,
        '--logger', "trx;LogFileName=$trxName"
    ) + $AdditionalArguments
    Invoke-Checked 'dotnet' $arguments
    $ended = [DateTimeOffset]::UtcNow
    $trxPath = Join-Path $resultDirectory $trxName
    if (-not (Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "$Name did not produce a TRX result."
    }
    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $summary = [ordered]@{
        aborted = [int]$counters.aborted
        buildInvocationId = $invocationId
        conclusion = if ([int]$counters.failed -eq 0 -and [int]$counters.error -eq 0 -and [int]$counters.timeout -eq 0 -and [int]$counters.aborted -eq 0) { 'Success' } else { 'Failure' }
        endedAtUtc = Format-UtcMilliseconds $ended
        error = [int]$counters.error
        executed = [int]$counters.executed
        failed = [int]$counters.failed
        gate = $Name
        passed = [int]$counters.passed
        sourceGitSha = $SourceGitSha
        startedAtUtc = Format-UtcMilliseconds $started
        timeout = [int]$counters.timeout
        total = [int]$counters.total
    }
    Remove-Item -LiteralPath $resultDirectory -Recurse -Force
    if ($summary.conclusion -ne 'Success') {
        throw "$Name gate did not conclude Success."
    }
    return [pscustomobject]$summary
}

function Remove-TestTrust($State) {
    if ($null -eq $State) { return }
    & certutil.exe -user -f -delstore Root $State.StoreThumbprint | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw 'Could not remove the P10 test certificate from CurrentUser/Root.'
    }
}

Push-Location $repositoryRoot
try {
    Write-Verbose 'P10 package set: checking source identity.'
    $head = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $head -cne $SourceGitSha) {
        throw 'SourceGitSha does not equal git rev-parse HEAD.'
    }

    Write-Verbose 'P10 package set: restoring solution.'
    Invoke-Checked 'dotnet' @('restore', 'CP6.Platform.sln')
    Write-Verbose 'P10 package set: building solution once.'
    Invoke-Checked 'dotnet' @(
        'build', 'CP6.Platform.sln',
        '--configuration', 'Release',
        '--no-restore',
        "-p:PackageVersion=$packageVersion",
        "-p:Version=$packageVersion",
        "-p:RepositoryCommit=$SourceGitSha",
        '-p:ContinuousIntegrationBuild=true'
    )

    Write-Verbose 'P10 package set: running sanitized gates.'
    $gateSummaries = @(
        Invoke-Gate 'ArchitectureTests' 'tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj' @()
        Invoke-Gate 'UnitTests' 'tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj' @()
        Invoke-Gate 'ReleaseTests' 'tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj' @('--filter', 'FullyQualifiedName!~P10PackageTests')
    )

    Write-Verbose 'P10 package set: packing exact seven-package set.'
    $packageSet = & (Join-Path $PSScriptRoot 'Pack-P10TestPackages.ps1') `
        -PackageVersion $packageVersion `
        -SourceGitSha $SourceGitSha `
        -OutputPath $privatePackages
    if (@($packageSet).Count -ne 1) {
        throw 'P10 pack script did not return one package-set record.'
    }

    Write-Verbose 'P10 package set: creating ephemeral test certificate.'
    $certificateState = & (Join-Path $PSScriptRoot 'New-P10TestCertificate.ps1') -OutputPath $privateRoot
    Write-Verbose 'P10 package set: signing and verifying fourteen package files.'
    $packageRecords = [Collections.Generic.List[object]]::new()
    $preSignHashes = [ordered]@{}
    $allPrivatePackages = @((Get-ChildItem -LiteralPath $privatePackages -Filter '*.nupkg' -File) +
        (Get-ChildItem -LiteralPath $privatePackages -Filter '*.snupkg' -File) | Sort-Object Name)
    foreach ($package in $allPrivatePackages) {
        $preSignHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        Invoke-Checked 'dotnet' @(
            'nuget', 'sign', $package.FullName,
            '--certificate-path', $certificateState.PfxPath,
            '--certificate-password', $certificateState.Password,
            '--hash-algorithm', 'SHA256',
            '--overwrite'
        )
        Invoke-Checked 'dotnet' @(
            'nuget', 'verify', $package.FullName,
            '--all',
            '--certificate-fingerprint', $certificateState.Fingerprint
        )
        $finalHash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $isSymbol = $package.Name.EndsWith('.snupkg', [StringComparison]::Ordinal)
        $packageId = $packageIds | Where-Object { $package.Name.StartsWith("$_`.$packageVersion.", [StringComparison]::Ordinal) }
        if (@($packageId).Count -ne 1) {
            throw "Could not identify package $($package.Name)."
        }
        if (-not $isSymbol) {
            $preSignHashes[$packageId] = $preSignHash
        }
        $packageRecords.Add([pscustomobject]@{
            File = $package.Name
            FinalHash = $finalHash
            IsSymbol = $isSymbol
            PackageId = $packageId
            PreSignHash = $preSignHash
        })
        if ($InjectFailureAfterSigning) {
            throw 'Injected failure after signing.'
        }
    }

    if ($packageRecords.Count -ne 14 -or $preSignHashes.Count -ne 7) {
        throw 'Signed package set is not exactly seven ordinary and seven symbol packages.'
    }

    foreach ($record in $packageRecords) {
        Copy-Item -LiteralPath (Join-Path $privatePackages $record.File) -Destination (Join-Path $resolvedOutput $record.File)
    }
    Copy-Item -LiteralPath $certificateState.CerPath -Destination (Join-Path $resolvedOutput 'test-signing-public.cer')

    $manifestPackages = @($packageIds | ForEach-Object {
        $id = $_
        $ordinary = $packageRecords | Where-Object { $_.PackageId -ceq $id -and -not $_.IsSymbol }
        $symbol = $packageRecords | Where-Object { $_.PackageId -ceq $id -and $_.IsSymbol }
        [ordered]@{
            certificateFingerprint = $certificateState.Fingerprint
            certificateSubject = 'CN=CP6 Platform P10 TEST ONLY'
            ordinaryFile = $ordinary.File
            ordinarySha256 = $ordinary.FinalHash
            packageId = $id
            sourceGitSha = $SourceGitSha
            symbolFile = $symbol.File
            symbolSha256 = $symbol.FinalHash
            testOnly = $true
            version = $packageVersion
        }
    })
    $manifest = [ordered]@{
        buildInvocationId = $invocationId
        certificateFingerprint = $certificateState.Fingerprint
        certificateSubject = 'CN=CP6 Platform P10 TEST ONLY'
        lockedRestore = [ordered]@{
            mode = 'locked'
            packageIds = $packageIds
            sourceMappingPattern = 'CP6.Platform.*'
            version = $packageVersion
        }
        packageVersion = $packageVersion
        packages = $manifestPackages
        platformSourceSha = $SourceGitSha
        profile = 'cp6-p10-test-package-set-v1'
        sourceRunAttempt = $RunAttempt
        sourceRunId = $RunId
        testOnly = $true
        timestampPolicy = 'TestOnlyNone'
    }

    $createdAtUtc = Format-UtcMilliseconds ([DateTimeOffset]::UtcNow)
    $provenance = [ordered]@{
        '$schemaId' = 'https://schemas.cp6.dev/release/build-invocation-provenance.v1'
        buildInvocationId = $invocationId
        createdAtUtc = $createdAtUtc
        finalPackages = @($packageIds | ForEach-Object {
            $id = $_
            $ordinary = $packageRecords | Where-Object { $_.PackageId -ceq $id -and -not $_.IsSymbol }
            [ordered]@{
                finalSha256 = $ordinary.FinalHash
                packageId = $id
                preSignSha256 = $ordinary.PreSignHash
                subject = [ordered]@{
                    sha256OrDigest = $ordinary.FinalHash
                    sourceGitSha = $SourceGitSha
                    subjectKind = 'Package'
                    subjectName = $id
                }
            }
        })
        preSignOutputs = @($packageIds | ForEach-Object {
            [ordered]@{ packageId = $_; sha256 = $preSignHashes[$_] }
        })
        sourceGitSha = $SourceGitSha
        toolchain = [ordered]@{
            dotnetSdk = (& dotnet --version).Trim()
            runner = 'windows'
        }
    }
    if ($LASTEXITCODE -ne 0) { throw 'Could not identify the .NET SDK.' }

    $releasePackage = $packageRecords | Where-Object { $_.PackageId -ceq 'CP6.Platform.Release' -and -not $_.IsSymbol }
    $releasePackagePath = Join-Path $resolvedOutput $releasePackage.File
    $evidence = [ordered]@{
        '$schemaId' = 'https://schemas.cp6.dev/release/evidence-record.v1'
        accessClass = 'TestOnly'
        conclusion = 'Success'
        createdAtUtc = $createdAtUtc
        evidenceKind = 'P10TestPackageGates'
        object = [ordered]@{
            byteLength = (Get-Item -LiteralPath $releasePackagePath).Length
            key = "objects/sha256/$($releasePackage.FinalHash.Substring(0, 2))/$($releasePackage.FinalHash)/test-only-evidence-record.v1.json"
            mediaType = 'application/vnd.cp6.evidence-record.v1+json'
            sha256 = $releasePackage.FinalHash
            storageAuthority = 'cp6-release-r2-v1'
        }
        policyVersion = 1
        producer = [ordered]@{
            commitSha = $SourceGitSha
            environment = 'test'
            repository = 'GTX537/CP6.Platform'
            runAttempt = $RunAttempt
            runId = $RunId
            workflowFileSha = $SourceGitSha
            workflowPath = '.github/workflows/p10-test-packages.yml'
        }
        subjects = @($packageIds | ForEach-Object {
            $id = $_
            $ordinary = $packageRecords | Where-Object { $_.PackageId -ceq $id -and -not $_.IsSymbol }
            [ordered]@{
                sha256OrDigest = $ordinary.FinalHash
                sourceGitSha = $SourceGitSha
                subjectKind = 'Package'
                subjectName = $id
            }
        })
    }
    $lockedRestore = [ordered]@{
        mode = 'locked'
        packageIds = $packageIds
        profile = 'cp6-p10-locked-restore-v1'
        sourceMappingPattern = 'CP6.Platform.*'
        version = $packageVersion
    }

    $documents = @(
        @{ Name = 'test-package-manifest.v1.json'; Value = $manifest; Validation = $null },
        @{ Name = 'build-invocation-provenance.v1.json'; Value = $provenance; Validation = 'validate-build-provenance' },
        @{ Name = 'test-only-evidence-record.v1.json'; Value = $evidence; Validation = 'validate-evidence' },
        @{ Name = 'locked-restore.v1.json'; Value = $lockedRestore; Validation = $null }
    )
    foreach ($document in $documents) {
        $rawPath = Join-Path $privateRaw $document.Name
        $finalPath = Join-Path $resolvedOutput $document.Name
        Write-RawJson $rawPath $document.Value
        Invoke-ReleaseTool @('canonicalize', $rawPath, $finalPath)
        if ($null -ne $document.Validation) {
            Invoke-ReleaseTool @($document.Validation, $finalPath)
        }
    }

    $gateOutput = Join-Path $resolvedOutput 'evidence\gates'
    [IO.Directory]::CreateDirectory($gateOutput) | Out-Null
    foreach ($gateSummary in $gateSummaries) {
        $fileName = "$($gateSummary.gate).v1.json"
        $rawPath = Join-Path $privateRaw $fileName
        Write-RawJson $rawPath $gateSummary
        Invoke-ReleaseTool @('canonicalize', $rawPath, (Join-Path $gateOutput $fileName))
    }

    $hashEntries = @(Get-ChildItem -LiteralPath $resolvedOutput -File -Recurse | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($resolvedOutput, $_.FullName).Replace('\', '/')
        [ordered]@{
            file = $relative
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object { $_.file })
    $hashDocument = [ordered]@{
        files = $hashEntries
        profile = 'cp6-p10-test-package-hashes-v1'
    }
    $rawHashes = Join-Path $privateRaw 'sha256.json'
    Write-RawJson $rawHashes $hashDocument
    Invoke-ReleaseTool @('canonicalize', $rawHashes, (Join-Path $resolvedOutput 'sha256.json'))

    Write-Host "Prepared the exact seven-package P10 test candidate $packageVersion."
}
finally {
    Write-Verbose 'P10 package set: cleaning private material and temporary trust.'
    Pop-Location
    Remove-TestTrust $certificateState
    if ($null -ne $certificateState) {
        $certificateState.Password = $null
    }
    if (Test-Path -LiteralPath $privateRoot) {
        Remove-Item -LiteralPath $privateRoot -Recurse -Force
    }
}
