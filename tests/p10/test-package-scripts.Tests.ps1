$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scriptPaths = @(
    'eng/p10/New-P10TestCertificate.ps1',
    'eng/p10/Pack-P10TestPackages.ps1',
    'eng/p10/New-P10TestPackageSet.ps1',
    'eng/p10/Test-P10TestPackageSet.ps1',
    'eng/p10/New-P10TransportRecord.ps1',
    'eng/p10/Test-P10TransportRecord.ps1'
)

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

foreach ($relativePath in $scriptPaths) {
    $path = Join-Path $repositoryRoot $relativePath
    Assert-True (Test-Path -LiteralPath $path -PathType Leaf) "$relativePath is missing."
    $text = Get-Content -LiteralPath $path -Raw
    Assert-True ($text -cmatch '(?m)^\[CmdletBinding\(\)\]\r?$') "$relativePath must declare CmdletBinding."
    Assert-True ($text -cmatch '(?m)^param\(\r?$') "$relativePath must declare explicit parameters."
    Assert-True ($text -cmatch "Set-StrictMode -Version Latest") "$relativePath must enable strict mode."
    Assert-True ($text -cmatch '\$ErrorActionPreference = ''Stop''') "$relativePath must stop on errors."
    Assert-True ($text -match 'artifacts[\\/]p10-test') "$relativePath must confine output to artifacts/p10-test/."
    Assert-True ($text -notmatch '(?i)nuget\s+push') "$relativePath must not publish packages."
    Assert-True ($text -notmatch '(?i)--skip-duplicate') "$relativePath must not hide duplicate packages."
    Assert-True ($text -notmatch '(?i)(api\.nuget\.org|nuget\.pkg\.github\.com|pkgs\.dev\.azure\.com)') "$relativePath must not name a formal feed."
    Assert-True ($text -notmatch '(?i)(wrangler\s+r2|aws\s+s3|rclone\s+.*r2:)') "$relativePath must not call R2 storage."
    Assert-True ($text -notmatch '(?i)(Set-Content|Out-File|WriteAllText).*password') "$relativePath must not persist a signing password."
}

$newPackageSet = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/New-P10TestPackageSet.ps1') -Raw
$newCertificate = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/New-P10TestCertificate.ps1') -Raw
$packPackages = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/Pack-P10TestPackages.ps1') -Raw
$verifyPackageSet = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/Test-P10TestPackageSet.ps1') -Raw
$transportRecord = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/New-P10TransportRecord.ps1') -Raw
$verifyTransportRecord = Get-Content -LiteralPath (Join-Path $repositoryRoot 'eng/p10/Test-P10TransportRecord.ps1') -Raw
$releaseTool = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tools/CP6.Platform.ReleaseTool/Program.cs') -Raw
$releaseToolProject = Get-Content -LiteralPath (Join-Path $repositoryRoot 'tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj') -Raw
Assert-True ($newPackageSet -cmatch '\$privateBuild = Join-Path \$privateRoot ''build''') 'P10 package creation must isolate its build output below the private root.'
Assert-True ([regex]::Matches($newPackageSet, '"-p:ArtifactsPath=\$privateBuild"').Count -ge 3) 'P10 restore, build, and gate commands must use the isolated artifacts path.'
Assert-True ($newPackageSet -cmatch '-BuildArtifactsPath \$privateBuild') 'P10 packing must consume the isolated build output.'
Assert-True ($newPackageSet -cmatch 'MSBUILDDISABLENODEREUSE') 'P10 nested builds must disable MSBuild node reuse so redirected process pipes can close.'
Assert-True ($packPackages -cmatch '\[string\]\$BuildArtifactsPath') 'P10 packing must require an explicit build artifacts path.'
Assert-True ($packPackages -cmatch '"-p:ArtifactsPath=\$resolvedBuildArtifacts"') 'P10 packing must preserve the isolated artifacts path.'
foreach ($certificateScript in @($newCertificate, $newPackageSet, $verifyPackageSet)) {
    Assert-True ($certificateScript -notmatch '(?i)(certutil|X509Store)') 'P10 test signing must not mutate the Windows root store.'
}
foreach ($verificationScript in @($newPackageSet, $verifyPackageSet)) {
    Assert-True ($verificationScript -notmatch 'Write-NuGetVerificationConfig') 'P10 verification must not rely on a NuGet configuration that the verify command ignores.'
    Assert-True ($verificationScript -cmatch "'verify-test-package'") 'P10 signature verification must call the isolated Release tool verifier.'
}
Assert-True ($verifyPackageSet -cmatch '\$verifyBuild = Join-Path \$verifyRoot ''build''') 'Independent package verification must isolate its Release tool build.'
Assert-True ([regex]::Matches($verifyPackageSet, '"-p:ArtifactsPath=\$verifyBuild"').Count -ge 2) 'Independent package verification must restore and build the Release tool in its private artifacts path.'
Assert-True ($verifyPackageSet -cmatch 'MSBUILDDISABLENODEREUSE') 'Independent package verification must disable MSBuild node reuse so redirected process pipes can close.'
Assert-True ($releaseToolProject -cmatch 'PackageReference Include="NuGet\.Packaging"') 'The Release tool must use the official NuGet package-verification API.'
Assert-True ($releaseTool -cmatch 'IntegrityVerificationProvider') 'The Release tool must verify signed-package archive integrity.'
Assert-True ($releaseTool -cmatch 'SignatureTrustAndValidityVerificationProvider') 'The Release tool must verify CMS validity while scoping untrusted-root allowance.'
Assert-True ($releaseTool -cmatch 'AllowListVerificationProvider') 'The Release tool must independently pin the author certificate fingerprint.'
Assert-True ($releaseTool -cmatch 'CertificateHashAllowListEntry') 'The Release tool must construct an exact certificate SHA-256 allow-list entry.'

Assert-True ($transportRecord -cmatch '\$transportBuild = Join-Path \$privateRoot ''build''') 'Transport creation must isolate its Release tool build.'
Assert-True ([regex]::Matches($transportRecord, '"-p:ArtifactsPath=\$transportBuild"').Count -ge 2) 'Transport creation must restore and build the Release tool in its private artifacts path.'
Assert-True ($transportRecord -cmatch 'MSBUILDDISABLENODEREUSE') 'Transport creation must disable MSBuild node reuse so redirected process pipes can close.'
Assert-True ($verifyTransportRecord -cmatch '\$transportVerifyBuild = Join-Path \$privateRoot ''build''') 'Independent transport verification must isolate its Release tool build.'
Assert-True ([regex]::Matches($verifyTransportRecord, '"-p:ArtifactsPath=\$transportVerifyBuild"').Count -ge 2) 'Independent transport verification must restore and build the Release tool in its private artifacts path.'
Assert-True ($verifyTransportRecord -cmatch 'MSBUILDDISABLENODEREUSE') 'Independent transport verification must disable MSBuild node reuse so redirected process pipes can close.'

Write-Host 'P10 test-package script contract tests passed.'
