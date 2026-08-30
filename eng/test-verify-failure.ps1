[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "cp6-platform-verify-$([Guid]::NewGuid().ToString('N'))"
$fakeBin = Join-Path $testRoot 'fake-bin'
$testEng = Join-Path $testRoot 'eng'

try {
    New-Item -ItemType Directory -Path $fakeBin, $testEng -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'verify.ps1') -Destination (Join-Path $testEng 'verify.ps1')
    Set-Content -LiteralPath (Join-Path $testRoot 'CP6.Platform.sln') -Value 'failure-contract fixture' -Encoding utf8

    if ($IsWindows) {
        Set-Content -LiteralPath (Join-Path $fakeBin 'dotnet.cmd') -Value '@exit /b 23' -Encoding ascii
    } else {
        $fakeDotNet = Join-Path $fakeBin 'dotnet'
        Set-Content -LiteralPath $fakeDotNet -Value "#!/usr/bin/env sh`nexit 23" -Encoding utf8
        & chmod +x $fakeDotNet
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not make the fake dotnet command executable.'
        }
    }

    $originalPath = $env:PATH
    $env:PATH = "$fakeBin$([IO.Path]::PathSeparator)$originalPath"
    try {
        foreach ($gate in @('Build', 'E2E', 'Contract')) {
            & pwsh (Join-Path $testEng 'verify.ps1') -Gate $gate -Profile failure-contract 2>&1 | Out-Null
            $verifyExitCode = $LASTEXITCODE
            if ($verifyExitCode -eq 0) {
                throw "The $gate gate unexpectedly succeeded when dotnet returned exit code 23."
            }

            $gateDirectory = $gate.ToLowerInvariant()
            $summaryPath = Join-Path $testRoot "artifacts/verify/$gateDirectory/summary.json"
            $junitPath = Join-Path $testRoot "artifacts/verify/$gateDirectory/results.junit.xml"
            if (-not (Test-Path -LiteralPath $summaryPath) -or -not (Test-Path -LiteralPath $junitPath)) {
                throw "The failed $gate gate did not create both summary.json and results.junit.xml."
            }

            $summary = Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
            if ($summary.schemaVersion -ne 1 -or $summary.gate -ne $gate -or $summary.status -ne 'Failed') {
                throw "The failed $gate summary does not match the version 1 failure contract."
            }

            [xml]$junit = Get-Content -LiteralPath $junitPath -Raw
            if ($junit.testsuite.failures -ne '1' -or $null -eq $junit.testsuite.testcase.failure) {
                throw "The failed $gate JUnit file does not contain one failure."
            }
        }
    } finally {
        $env:PATH = $originalPath
    }

    & pwsh (Join-Path $testEng 'verify.ps1') -Gate Performance -Profile not-applicable-contract 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw 'The NotApplicable Performance gate returned a non-zero exit code.'
    }

    $notApplicableSummary = Get-Content -LiteralPath (Join-Path $testRoot 'artifacts/verify/performance/summary.json') -Raw |
        ConvertFrom-Json
    if ($notApplicableSummary.schemaVersion -ne 1 -or $notApplicableSummary.status -ne 'NotApplicable' -or
        [string]::IsNullOrWhiteSpace($notApplicableSummary.checks[0].reason)) {
        throw 'The Performance summary does not match the version 1 NotApplicable contract.'
    }

    [xml]$notApplicableJunit = Get-Content -LiteralPath (Join-Path $testRoot 'artifacts/verify/performance/results.junit.xml') -Raw
    if ($notApplicableJunit.testsuite.skipped -ne '1' -or $null -eq $notApplicableJunit.testsuite.testcase.skipped) {
        throw 'The NotApplicable Performance JUnit file does not contain one skipped test and reason.'
    }
} finally {
    if ($testRoot.StartsWith([IO.Path]::GetTempPath(), [StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $testRoot)) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'Verification failure-contract self-test passed.'
exit 0
