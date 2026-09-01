$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$modulePath = Join-Path $repositoryRoot 'eng\p09\P09Rehearsal.psm1'
$fakeDocker = Join-Path $repositoryRoot 'tests\p09\fake-docker\docker.ps1'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if (($Expected | ConvertTo-Json -Compress -Depth 20) -cne ($Actual | ConvertTo-Json -Compress -Depth 20)) {
        throw "$Message`nExpected: $($Expected | ConvertTo-Json -Compress -Depth 20)`nActual: $($Actual | ConvertTo-Json -Compress -Depth 20)"
    }
}

Assert-True (Test-Path -LiteralPath $modulePath -PathType Leaf) 'P09 rehearsal module is missing.'
Import-Module $modulePath -Force

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("cp6-p09-cleanup-tests-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $compose = Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'
    $project = 'cp6-p09-abcdef0123456789'
    $log = Join-Path $testRoot 'docker.jsonl'
    $responses = Join-Path $testRoot 'responses.jsonl'
    $env:CP6_P09_FAKE_DOCKER_LOG = $log
    $runtimeRoot = Join-Path $testRoot 'runtime-root'
    $releaseDirectories = @(
        'dapr/publisher/components',
        'dapr/receiver/components',
        'dapr/unauthorized/components'
    )
    foreach ($relative in $releaseDirectories) {
        [IO.Directory]::CreateDirectory((Join-Path $runtimeRoot $relative)) | Out-Null
    }
    @(1..8 | ForEach-Object { @{ exitCode = 0; stdout = ''; stderr = '' } }) |
        ForEach-Object { $_ | ConvertTo-Json -Compress } |
        Set-Content -LiteralPath $responses -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $responses

    $releasedTeardown = Invoke-Cp6P09Teardown `
        -DockerCommand $fakeDocker `
        -ProjectName $project `
        -ComposeFile $compose `
        -RepositoryRoot $repositoryRoot `
        -RuntimeRoot $runtimeRoot `
        -CleanupUser '1001:1001'
    Assert-Equal $null $releasedTeardown.CleanupFailureId 'Successful target-owned directory release was reported as failed.'
    $releaseCalls = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 8 $releaseCalls.Count 'Teardown must down Compose, release three watch directories, then query four residue classes.'
    foreach ($index in 0..2) {
        $expected = Get-Cp6P09DirectoryReleaseDockerArguments `
            -ProjectName $project `
            -ComposeFile $compose `
            -Directory (Join-Path $runtimeRoot $releaseDirectories[$index]) `
            -User '1001:1001'
        Assert-Equal $expected @($releaseCalls[$index + 1].argv) "Watch-directory release call $index drifted."
    }
    Assert-Equal @('container','ls','--all','--quiet','--filter',"label=com.docker.compose.project=$project") @($releaseCalls[4].argv) 'Residue queries did not run after ownership release.'

    Remove-Item -LiteralPath $log -Force
    Remove-Item -LiteralPath "$log.index" -Force -ErrorAction SilentlyContinue
    @(
        @{ exitCode = 19; stdout = ''; stderr = 'runtime-failed' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $responses -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $responses

    $result = Invoke-Cp6P09GuardedDockerFailure -DockerCommand $fakeDocker -ProjectName $project -ComposeFile $compose
    Assert-Equal 'Failed' $result.Status 'Runtime failure must remain Failed.'
    Assert-Equal 'runtime-command' $result.OriginalFailureId 'Original runtime error was not preserved separately.'
    Assert-Equal $null $result.CleanupFailureId 'Successful cleanup was reported as failed.'

    $calls = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal @('compose','--project-name',$project,'--file',$compose,'--profile','negative','--profile','provision','down','--volumes','--remove-orphans','--rmi','local') @($calls[1].argv) 'The first cleanup command did not include every resource-bearing profile.'
    foreach ($call in $calls) {
        $flat = @($call.argv)
        Assert-True (-not ($flat -contains 'prune')) 'Runner invoked prune.'
        if ($flat -contains 'ps' -or $flat -contains 'network' -or $flat -contains 'volume' -or $flat -contains 'image') {
            Assert-True (($flat -join ' ') -match [Regex]::Escape("com.docker.compose.project=$project")) 'Resource query omitted the exact project label.'
        }
    }

    Remove-Item -LiteralPath $log -Force
    Remove-Item -LiteralPath "$log.index" -Force -ErrorAction SilentlyContinue
    @(
        @{ exitCode = 23; stdout = ''; stderr = 'runtime-failed' }
        @{ exitCode = 31; stdout = ''; stderr = 'cleanup-failed' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $responses -Encoding utf8NoBOM

    $cleanupFailure = Invoke-Cp6P09GuardedDockerFailure -DockerCommand $fakeDocker -ProjectName $project -ComposeFile $compose
    Assert-Equal 'Failed' $cleanupFailure.Status 'Cleanup failure must force Failed.'
    Assert-Equal 'runtime-command' $cleanupFailure.OriginalFailureId 'Cleanup failure erased the original failure.'
    Assert-Equal 'compose-down' $cleanupFailure.CleanupFailureId 'Cleanup failure was not reported separately.'

    $calls = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-True ($calls.Count -ge 6) 'Cleanup stopped after compose down failure instead of checking residue.'

    Remove-Item -LiteralPath $log -Force
    Remove-Item -LiteralPath "$log.index" -Force -ErrorAction SilentlyContinue
    $residualId = 'deadbeefcafe'
    $labels = [ordered]@{
        'com.docker.compose.project' = $project
        'com.docker.compose.project.config_files' = [IO.Path]::GetFullPath($compose)
    } | ConvertTo-Json -Compress
    @(
        @{ exitCode = 47; stdout = ''; stderr = 'runtime-failed' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = "$residualId`n"; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = $labels; stderr = '' }
        @{ exitCode = 0; stdout = $residualId; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $responses -Encoding utf8NoBOM

    $oneOffCleanup = Invoke-Cp6P09GuardedDockerFailure -DockerCommand $fakeDocker -ProjectName $project -ComposeFile $compose
    Assert-Equal 'Failed' $oneOffCleanup.Status 'The original runtime failure must remain Failed after one-off cleanup.'
    Assert-Equal 'runtime-command' $oneOffCleanup.OriginalFailureId 'One-off cleanup erased the original failure.'
    Assert-Equal $null $oneOffCleanup.CleanupFailureId 'Verified one-off cleanup was reported as failed.'
    $calls = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal @('container','inspect','--format','{{json .Config.Labels}}',$residualId) @($calls[6].argv) 'Residual container identity was not inspected before removal.'
    Assert-Equal @('container','rm','--force','--volumes',$residualId) @($calls[7].argv) 'Verified one-off cleanup did not remove the exact container and its anonymous volumes.'
    Assert-Equal @('compose','--project-name',$project,'--file',$compose,'--profile','negative','--profile','provision','down','--volumes','--remove-orphans','--rmi','local') @($calls[8].argv) 'One-off cleanup did not repeat the all-profile Compose down.'

    Remove-Item -LiteralPath $log -Force
    Remove-Item -LiteralPath "$log.index" -Force -ErrorAction SilentlyContinue
    $mixedLabels = [ordered]@{
        'com.docker.compose.project' = 'cp6-p09-ffffffffffffffff'
        'com.docker.compose.project.config_files' = [IO.Path]::GetFullPath($compose)
    } | ConvertTo-Json -Compress
    @(
        @{ exitCode = 53; stdout = ''; stderr = 'runtime-failed' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = "$residualId`n"; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = $mixedLabels; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = "$residualId`n"; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $responses -Encoding utf8NoBOM

    $mixedCleanup = Invoke-Cp6P09GuardedDockerFailure -DockerCommand $fakeDocker -ProjectName $project -ComposeFile $compose
    Assert-Equal 'residue-identity' $mixedCleanup.CleanupFailureId 'Mixed-label residue was not rejected.'
    $calls = @(Get-Content -LiteralPath $log | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-True (-not @($calls | Where-Object { @($_.argv) -contains 'rm' -and @($_.argv) -contains $residualId }).Count) 'Mixed-label residue was deleted.'
}
finally {
    Remove-Item Env:CP6_P09_FAKE_DOCKER_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:CP6_P09_FAKE_DOCKER_RESPONSES -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'P09 cleanup failure script tests passed.'
