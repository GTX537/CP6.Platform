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

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p10-formal-bootstrap-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
$originalState = [Environment]::GetEnvironmentVariable('CP6_TEST_GH_STATE')
$originalFailure = [Environment]::GetEnvironmentVariable('CP6_TEST_GH_FAIL_WRITE')
$originalNodeReuse = [Environment]::GetEnvironmentVariable('MSBUILDDISABLENODEREUSE')

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
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'P10 formal certificate bootstrap tests passed.'
