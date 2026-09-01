[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{40}$')]
    [string]$ExpectedSourceGitSha,

    [Parameter(Mandatory)]
    [ValidateRange(1, [long]::MaxValue)]
    [long]$ExpectedRunId,

    [Parameter(Mandatory)]
    [ValidateRange(1, [int]::MaxValue)]
    [int]$ExpectedRunAttempt,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-f]{64}$')]
    [string]$ExpectedCertificateFingerprint
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $IsWindows) {
    throw 'P10 test package-set verification requires Windows.'
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts\p10-test'))
$resolvedPackages = [IO.Path]::GetFullPath($PackagePath, $repositoryRoot)
$artifactsPrefix = $artifactsRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $resolvedPackages.StartsWith($artifactsPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Package verification input must be below $artifactsRoot."
}
if (-not (Test-Path -LiteralPath $resolvedPackages -PathType Container)) {
    throw 'P10 test package-set directory does not exist.'
}

$packageIds = @(
    'CP6.Platform.Abstractions',
    'CP6.Platform.AspNetCore',
    'CP6.Platform.Contracts',
    'CP6.Platform.Deployment',
    'CP6.Platform.EntityFramework',
    'CP6.Platform.Messaging',
    'CP6.Platform.Release'
)
$expectedVersion = "0.10.0-test.$($ExpectedSourceGitSha.Substring(0, 12)).$ExpectedRunAttempt"
$expectedInvocation = "p10-s02:$ExpectedSourceGitSha`:$ExpectedRunId`:$ExpectedRunAttempt"
$certificate = $null
$trustWasAdded = $false
$verifyRoot = Join-Path $artifactsRoot ("private\verify-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($verifyRoot) | Out-Null

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if (($Expected | ConvertTo-Json -Compress -Depth 30) -cne ($Actual | ConvertTo-Json -Compress -Depth 30)) {
        throw "$Message`nExpected: $($Expected | ConvertTo-Json -Compress -Depth 30)`nActual: $($Actual | ConvertTo-Json -Compress -Depth 30)"
    }
}

function Assert-ExactProperties($Object, [string[]]$Expected, [string]$Name) {
    Assert-Equal @($Expected | Sort-Object) @($Object.PSObject.Properties.Name | Sort-Object) "$Name contains missing or unknown fields."
}

function Invoke-Checked([string]$FileName, [string[]]$Arguments) {
    & $FileName @Arguments | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName failed with exit code $LASTEXITCODE."
    }
}

function Invoke-ReleaseTool([string[]]$Arguments) {
    $toolPath = Join-Path $repositoryRoot 'tools\CP6.Platform.ReleaseTool\bin\Release\net8.0\CP6.Platform.ReleaseTool.dll'
    Assert-True (Test-Path -LiteralPath $toolPath -PathType Leaf) 'Release tool build output is missing.'
    Invoke-Checked 'dotnet' (@($toolPath) + $Arguments)
}

function Assert-CanonicalJson([string]$Path) {
    $canonicalPath = Join-Path $verifyRoot ([Guid]::NewGuid().ToString('N') + '.json')
    Invoke-ReleaseTool @('canonicalize', $Path, $canonicalPath)
    $actual = [IO.File]::ReadAllBytes($Path)
    $canonical = [IO.File]::ReadAllBytes($canonicalPath)
    Assert-True ([Convert]::ToHexString($actual) -ceq [Convert]::ToHexString($canonical)) "$([IO.Path]::GetFileName($Path)) is not canonical JSON."
}

function Get-PackageMetadata([string]$Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $nuspec = @($archive.Entries | Where-Object { $_.FullName.EndsWith('.nuspec', [StringComparison]::Ordinal) })
        Assert-True ($nuspec.Count -eq 1) "$([IO.Path]::GetFileName($Path)) must contain one nuspec."
        $stream = $nuspec[0].Open()
        $reader = [IO.StreamReader]::new($stream)
        try { [xml]$document = $reader.ReadToEnd() }
        finally { $reader.Dispose(); $stream.Dispose() }
        $metadata = $document.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
        $id = $metadata.SelectSingleNode("*[local-name()='id']").InnerText
        $version = $metadata.SelectSingleNode("*[local-name()='version']").InnerText
        $repository = $metadata.SelectSingleNode("*[local-name()='repository']")
        return [pscustomobject]@{
            Id = $id
            Version = $version
            Commit = if ($null -eq $repository) { $null } else { $repository.GetAttribute('commit') }
        }
    }
    finally {
        $archive.Dispose()
    }
}

Push-Location $repositoryRoot
try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $expectedPackageFiles = @($packageIds | ForEach-Object {
        "$($_).$expectedVersion.nupkg"
        "$($_).$expectedVersion.snupkg"
    } | Sort-Object)
    $expectedFiles = @(
        $expectedPackageFiles
        'build-invocation-provenance.v1.json'
        'evidence/gates/ArchitectureTests.v1.json'
        'evidence/gates/ReleaseTests.v1.json'
        'evidence/gates/UnitTests.v1.json'
        'locked-restore.v1.json'
        'sha256.json'
        'test-only-evidence-record.v1.json'
        'test-package-manifest.v1.json'
        'test-signing-public.cer'
    ) | Sort-Object
    $actualFiles = @(Get-ChildItem -LiteralPath $resolvedPackages -File -Recurse | ForEach-Object {
        [IO.Path]::GetRelativePath($resolvedPackages, $_.FullName).Replace('\', '/')
    } | Sort-Object)
    Assert-Equal $expectedFiles $actualFiles 'P10 package artifact file set is not exact.'
    Assert-True (-not ($actualFiles -match 'CP6\.Platform\.Testing')) 'Repository-only Testing package was included.'

    $certificatePath = Join-Path $resolvedPackages 'test-signing-public.cer'
    $certificateBytes = [IO.File]::ReadAllBytes($certificatePath)
    $actualFingerprint = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificateBytes)).ToLowerInvariant()
    Assert-Equal $ExpectedCertificateFingerprint $actualFingerprint 'Test signing certificate fingerprint differs.'
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($certificateBytes)
    Assert-Equal 'CN=CP6 Platform P10 TEST ONLY' $certificate.Subject 'Test certificate subject differs.'
    & certutil.exe -user -f -addstore Root $certificatePath | Out-Host
    if ($LASTEXITCODE -ne 0) { throw 'Could not add the test certificate to CurrentUser/Root.' }
    $trustWasAdded = $true

    foreach ($fileName in $expectedPackageFiles) {
        $path = Join-Path $resolvedPackages $fileName
        Invoke-Checked 'dotnet' @(
            'nuget', 'verify', $path,
            '--all',
            '--certificate-fingerprint', $ExpectedCertificateFingerprint
        )
        $metadata = Get-PackageMetadata $path
        $expectedId = $packageIds | Where-Object { $fileName.StartsWith("$_`.$expectedVersion.", [StringComparison]::Ordinal) }
        Assert-Equal $expectedId $metadata.Id "$fileName package ID differs."
        Assert-Equal $expectedVersion $metadata.Version "$fileName package version differs."
        Assert-Equal $ExpectedSourceGitSha $metadata.Commit "$fileName repository commit differs."
    }

    $manifestPath = Join-Path $resolvedPackages 'test-package-manifest.v1.json'
    Assert-CanonicalJson $manifestPath
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    Assert-ExactProperties $manifest @(
        'buildInvocationId','certificateFingerprint','certificateSubject','lockedRestore','packageVersion','packages',
        'platformSourceSha','profile','sourceRunAttempt','sourceRunId','testOnly','timestampPolicy'
    ) 'Test package manifest'
    Assert-Equal 'cp6-p10-test-package-set-v1' $manifest.profile 'Manifest profile differs.'
    Assert-True ([bool]$manifest.testOnly) 'Manifest is not marked test-only.'
    Assert-Equal $ExpectedSourceGitSha $manifest.platformSourceSha 'Manifest source differs.'
    Assert-Equal $ExpectedRunId $manifest.sourceRunId 'Manifest run ID differs.'
    Assert-Equal $ExpectedRunAttempt $manifest.sourceRunAttempt 'Manifest run attempt differs.'
    Assert-Equal $expectedInvocation $manifest.buildInvocationId 'Manifest invocation differs.'
    Assert-Equal $expectedVersion $manifest.packageVersion 'Manifest version differs.'
    Assert-Equal 'CN=CP6 Platform P10 TEST ONLY' $manifest.certificateSubject 'Manifest certificate subject differs.'
    Assert-Equal $ExpectedCertificateFingerprint $manifest.certificateFingerprint 'Manifest certificate fingerprint differs.'
    Assert-Equal 'TestOnlyNone' $manifest.timestampPolicy 'Manifest timestamp policy differs.'
    Assert-ExactProperties $manifest.lockedRestore @('mode','packageIds','sourceMappingPattern','version') 'Manifest locked restore'
    Assert-Equal 'locked' $manifest.lockedRestore.mode 'Locked restore mode differs.'
    Assert-Equal 'CP6.Platform.*' $manifest.lockedRestore.sourceMappingPattern 'Source mapping pattern differs.'
    Assert-Equal $packageIds @($manifest.lockedRestore.packageIds) 'Locked restore package IDs differ.'
    Assert-Equal $expectedVersion $manifest.lockedRestore.version 'Locked restore version differs.'
    Assert-Equal $packageIds @($manifest.packages.packageId) 'Manifest package IDs differ.'
    foreach ($package in $manifest.packages) {
        Assert-ExactProperties $package @(
            'certificateFingerprint','certificateSubject','ordinaryFile','ordinarySha256','packageId','sourceGitSha',
            'symbolFile','symbolSha256','testOnly','version'
        ) "Manifest package $($package.packageId)"
        Assert-True ([bool]$package.testOnly) "$($package.packageId) is not marked test-only."
        Assert-Equal $expectedVersion $package.version "$($package.packageId) version differs."
        Assert-Equal $ExpectedSourceGitSha $package.sourceGitSha "$($package.packageId) source differs."
        Assert-Equal 'CN=CP6 Platform P10 TEST ONLY' $package.certificateSubject "$($package.packageId) certificate subject differs."
        Assert-Equal $ExpectedCertificateFingerprint $package.certificateFingerprint "$($package.packageId) fingerprint differs."
        Assert-Equal "$($package.packageId).$expectedVersion.nupkg" $package.ordinaryFile "$($package.packageId) ordinary filename differs."
        Assert-Equal "$($package.packageId).$expectedVersion.snupkg" $package.symbolFile "$($package.packageId) symbol filename differs."
    }

    $lockedPath = Join-Path $resolvedPackages 'locked-restore.v1.json'
    Assert-CanonicalJson $lockedPath
    $locked = Get-Content -LiteralPath $lockedPath -Raw | ConvertFrom-Json
    Assert-ExactProperties $locked @('mode','packageIds','profile','sourceMappingPattern','version') 'Locked restore metadata'
    Assert-Equal 'cp6-p10-locked-restore-v1' $locked.profile 'Locked restore profile differs.'
    Assert-Equal 'locked' $locked.mode 'Locked restore mode differs.'
    Assert-Equal $packageIds @($locked.packageIds) 'Locked restore package IDs differ.'
    Assert-Equal 'CP6.Platform.*' $locked.sourceMappingPattern 'Locked restore source mapping differs.'
    Assert-Equal $expectedVersion $locked.version 'Locked restore exact version differs.'

    $provenancePath = Join-Path $resolvedPackages 'build-invocation-provenance.v1.json'
    $evidencePath = Join-Path $resolvedPackages 'test-only-evidence-record.v1.json'
    Assert-CanonicalJson $provenancePath
    Assert-CanonicalJson $evidencePath
    Invoke-ReleaseTool @('validate-build-provenance', $provenancePath)
    Invoke-ReleaseTool @('validate-evidence', $evidencePath)
    $provenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    Assert-Equal $ExpectedSourceGitSha $provenance.sourceGitSha 'Provenance source differs.'
    Assert-Equal $expectedInvocation $provenance.buildInvocationId 'Provenance invocation differs.'
    Assert-Equal $packageIds @($provenance.finalPackages.packageId) 'Provenance package set differs.'
    $evidence = Get-Content -LiteralPath $evidencePath -Raw | ConvertFrom-Json
    Assert-Equal 'TestOnly' $evidence.accessClass 'Evidence access class is not TestOnly.'
    Assert-Equal 'Success' $evidence.conclusion 'Evidence conclusion differs.'
    Assert-Equal $ExpectedSourceGitSha $evidence.producer.commitSha 'Evidence source differs.'
    Assert-Equal $ExpectedRunId $evidence.producer.runId 'Evidence run ID differs.'
    Assert-Equal $ExpectedRunAttempt $evidence.producer.runAttempt 'Evidence run attempt differs.'
    Assert-Equal $packageIds @($evidence.subjects.subjectName) 'Evidence subjects differ.'

    foreach ($gateName in @('ArchitectureTests','ReleaseTests','UnitTests')) {
        $gatePath = Join-Path $resolvedPackages "evidence\gates\$gateName.v1.json"
        Assert-CanonicalJson $gatePath
        $gate = Get-Content -LiteralPath $gatePath -Raw | ConvertFrom-Json
        Assert-ExactProperties $gate @(
            'aborted','buildInvocationId','conclusion','endedAtUtc','error','executed','failed','gate','passed',
            'sourceGitSha','startedAtUtc','timeout','total'
        ) "$gateName summary"
        Assert-Equal $gateName $gate.gate "$gateName gate name differs."
        Assert-Equal $ExpectedSourceGitSha $gate.sourceGitSha "$gateName source differs."
        Assert-Equal $expectedInvocation $gate.buildInvocationId "$gateName invocation differs."
        Assert-Equal 'Success' $gate.conclusion "$gateName did not pass."
        Assert-True ($gate.failed -eq 0 -and $gate.error -eq 0 -and $gate.timeout -eq 0 -and $gate.aborted -eq 0) "$gateName has failure counters."
    }

    $hashPath = Join-Path $resolvedPackages 'sha256.json'
    Assert-CanonicalJson $hashPath
    $hashes = Get-Content -LiteralPath $hashPath -Raw | ConvertFrom-Json
    Assert-ExactProperties $hashes @('files','profile') 'SHA-256 manifest'
    Assert-Equal 'cp6-p10-test-package-hashes-v1' $hashes.profile 'SHA-256 manifest profile differs.'
    $expectedHashedFiles = @($expectedFiles | Where-Object { $_ -cne 'sha256.json' })
    Assert-Equal $expectedHashedFiles @($hashes.files.file) 'SHA-256 manifest file set differs.'
    foreach ($entry in $hashes.files) {
        Assert-ExactProperties $entry @('file','sha256') "SHA-256 entry $($entry.file)"
        $actual = (Get-FileHash -LiteralPath (Join-Path $resolvedPackages $entry.file) -Algorithm SHA256).Hash.ToLowerInvariant()
        Assert-Equal $actual $entry.sha256 "$($entry.file) final bytes differ from sha256.json."
    }
}
finally {
    Pop-Location
    if ($trustWasAdded -and $null -ne $certificate) {
        & certutil.exe -user -f -delstore Root $certificate.Thumbprint | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Could not remove the test certificate from CurrentUser/Root.' }
    }
    if ($null -ne $certificate) { $certificate.Dispose() }
    if (Test-Path -LiteralPath $verifyRoot) {
        Remove-Item -LiteralPath $verifyRoot -Recurse -Force
    }
}

Write-Host "Verified the exact seven-package P10 test candidate $expectedVersion."
