$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$modulePath = Join-Path $repositoryRoot 'eng\p09\P09Rehearsal.psm1'
$runnerPath = Join-Path $repositoryRoot 'eng\run-p09-compose-rehearsal.ps1'
$fakeDocker = Join-Path $repositoryRoot 'tests\p09\fake-docker\docker.ps1'

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string]$Message) {
    if (($Expected | ConvertTo-Json -Compress -Depth 20) -cne ($Actual | ConvertTo-Json -Compress -Depth 20)) {
        throw "$Message`nExpected: $($Expected | ConvertTo-Json -Compress -Depth 20)`nActual: $($Actual | ConvertTo-Json -Compress -Depth 20)"
    }
}

function Assert-Throws([scriptblock]$Action, [string]$MessagePattern) {
    try { & $Action }
    catch {
        if ($_.Exception.Message -notmatch $MessagePattern) {
            throw "Unexpected exception: $($_.Exception.Message)"
        }
        return
    }
    throw "Expected exception matching '$MessagePattern'."
}

Assert-True (Test-Path -LiteralPath $modulePath -PathType Leaf) 'P09 rehearsal module is missing.'
Assert-True (Test-Path -LiteralPath $runnerPath -PathType Leaf) 'P09 rehearsal runner is missing.'
Import-Module $modulePath -Force

$runnerText = [IO.File]::ReadAllText($runnerPath, [Text.Encoding]::UTF8)
$moduleText = [IO.File]::ReadAllText($modulePath, [Text.Encoding]::UTF8)
Assert-True ($runnerText -match '(?s)param\(\s*\[string\]\$ProfilePath\s*=\s*"contracts/p09/examples/non-production-runtime-profile.valid.json",\s*\[string\]\$ArtifactsRoot\s*=\s*"artifacts/p09-rehearsal",\s*\[string\]\$ExpectedGitSha,\s*\[switch\]\$KeepFailedArtifacts\s*\)') 'Runner parameters drifted from the approved interface.'
    Assert-True ($moduleText -notmatch '(?i)[A-Za-z]:[\\/]Users[\\/]') 'Runner module contains a machine-specific user path.'
    Assert-True ($moduleText.Contains('$actualCompose.Equals($compose, (Get-Cp6P09PathComparison))')) 'Teardown Compose-label validation is not platform case-sensitive.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("cp6-p09-tests-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    $layout = New-Cp6P09RunLayout -RepositoryRoot $repositoryRoot -ArtifactsRoot 'artifacts/p09-rehearsal'
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $resolvedRuntime = [IO.Path]::GetFullPath($layout.RuntimeRoot)
    Assert-True ($resolvedRuntime.StartsWith($tempRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Runtime root is not a separator-safe child of the OS temp root.'
    Assert-True ($layout.ArtifactsDirectory.StartsWith((Join-Path $repositoryRoot 'artifacts\p09-rehearsal') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) 'Artifact directory escaped its approved root.'
    Assert-True ($layout.ArtifactReference -cmatch '^artifacts/p09-rehearsal/[0-9]{8}T[0-9]{6}Z-[a-f0-9]{16}$') 'Artifact result reference is not a repository-relative stable path.'
    Assert-Cp6P09SafeText -Text $layout.ArtifactReference
    Remove-Cp6P09ExactTree -Path $layout.RuntimeRoot -AllowedRoot ([IO.Path]::GetTempPath())

    Assert-Throws {
        Resolve-Cp6P09ContainedPath -Root (Join-Path $testRoot 'runtime') -Candidate (Join-Path $testRoot 'runtime-escape') -RequireChild
    } 'outside|escape|contained'

    if (-not $IsWindows) {
        $linkRoot = Join-Path $testRoot 'links'
        $outside = Join-Path $testRoot 'outside'
        [IO.Directory]::CreateDirectory($linkRoot) | Out-Null
        [IO.Directory]::CreateDirectory($outside) | Out-Null
        $link = Join-Path $linkRoot 'escape'
        New-Item -ItemType SymbolicLink -Path $link -Target $outside | Out-Null
        Assert-Throws {
            Resolve-Cp6P09ContainedPath -Root $linkRoot -Candidate (Join-Path $link 'child') -RequireChild
        } 'reparse|symbolic|outside|escape'
    }

    $fakeLog = Join-Path $testRoot 'argv.jsonl'
    $env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
    $injectionArguments = @('compose', '--project-name', 'cp6-p09-safe', '--file', 'canonical path.yaml', 'config', '; Remove-Item *', '$(Get-ChildItem)')
    $processResult = Invoke-Cp6P09Process -FilePath $fakeDocker -ArgumentList $injectionArguments -TimeoutSeconds 10 -MaximumOutputBytes 4096
    Assert-Equal 0 $processResult.ExitCode 'Fake docker invocation failed.'
    $record = (Get-Content -LiteralPath $fakeLog | Select-Object -Last 1) | ConvertFrom-Json
    Assert-Equal $injectionArguments @($record.argv) 'Process arguments were not passed as an injection-safe argument array.'

    $secretStdin = 'this-value-must-not-appear-in-argv-or-log'
    $stdinResult = Invoke-Cp6P09Process -FilePath $fakeDocker -ArgumentList @('compose','run','kafka-admin') -StandardInput $secretStdin -TimeoutSeconds 10 -MaximumOutputBytes 4096
    Assert-Equal 0 $stdinResult.ExitCode 'STDIN-only fake docker invocation failed.'
    $stdinRecord = (Get-Content -LiteralPath $fakeLog | Select-Object -Last 1) | ConvertFrom-Json
    Assert-Equal ([Text.Encoding]::UTF8.GetByteCount($secretStdin)) $stdinRecord.stdinBytes 'Generated content was not delivered through STDIN.'
    Assert-True ((Get-Content -Raw -LiteralPath $fakeLog) -notmatch [Regex]::Escape($secretStdin)) 'Generated secret entered argv or the fake Docker log.'

    $largeResponse = Join-Path $testRoot 'large-response.jsonl'
    @{ exitCode = 0; stdout = ('x' * 5000); stderr = '' } | ConvertTo-Json -Compress | Set-Content -LiteralPath $largeResponse -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $largeResponse
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    Assert-Throws {
        Invoke-Cp6P09Process -FilePath $fakeDocker -ArgumentList @('version') -TimeoutSeconds 10 -MaximumOutputBytes 128
    } 'output|bounded|limit'

    foreach ($unsafe in @('password=super-secret-value', 'Bearer abcdefghijklmnop', 'C:\Users\someone\secret.txt', '/home/runner/secret.txt')) {
        Assert-Throws { Assert-Cp6P09SafeText -Text $unsafe } 'unsafe|sensitive|secret|path'
    }

    $credentials = New-Cp6P09CredentialSet
    Assert-Equal @('Provisioner','Publisher','Receiver','Unauthorized') @($credentials.PSObject.Properties.Name) 'Credential roles drifted.'
    $credentialValues = @($credentials.PSObject.Properties.Value)
    Assert-Equal 4 @($credentialValues | Select-Object -Unique).Count 'Credentials are not independent.'
    foreach ($credential in $credentialValues) {
        Assert-True ($credential -cmatch '^[A-Za-z0-9_-]{43}$') 'Credential is not 32-byte Base64URL without padding.'
    }

    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    Test-Cp6P09ForeignTopic -Topic 'cp6.platform.deployment-probe.v1' -DockerCommand $fakeDocker | Out-Null
    Assert-Throws {
        Test-Cp6P09ForeignTopic -Topic 'cp6.platform.other.v1' -DockerCommand $fakeDocker
    } 'foreign-topic-denied'
    Assert-True (-not (Test-Path -LiteralPath $fakeLog)) 'Foreign Topic rejection reached Docker.'
    $topicListBefore = @('cp6.platform.deployment-probe.v1')
    $dockerCallsBefore = @(Get-Content -LiteralPath $fakeLog -ErrorAction SilentlyContinue).Count
    Assert-Cp6P09ForeignTopicRejected
    $dockerCallsAfter = @(Get-Content -LiteralPath $fakeLog -ErrorAction SilentlyContinue).Count
    $topicListAfter = @('cp6.platform.deployment-probe.v1')
    Assert-Equal $dockerCallsBefore $dockerCallsAfter 'Foreign Topic assertion issued a Docker command.'
    Assert-Equal $topicListBefore $topicListAfter 'Foreign Topic assertion mutated the broker Topic set.'

    $canonicalSample = ConvertTo-Cp6P09CanonicalJson -Value ([ordered]@{ z = 1; a = [ordered]@{ y = $true; b = 'x' } })
    Assert-Equal '{"a":{"b":"x","y":true},"z":1}' $canonicalSample 'Canonical JSON property ordering drifted.'
    $validEvidenceHash = Test-Cp6P09Evidence -RepositoryRoot $repositoryRoot -EvidencePath (Join-Path $repositoryRoot 'contracts\p09\examples\rehearsal-evidence.valid.json')
    Assert-True ($validEvidenceHash -cmatch '^[0-9a-f]{64}$') 'Evidence validator did not return a lowercase SHA-256.'
    Assert-Throws {
        Test-Cp6P09Evidence -RepositoryRoot $repositoryRoot -EvidencePath (Join-Path $repositoryRoot 'contracts\p09\examples\rehearsal-evidence.secret.invalid.json')
    } 'evidence|contract|invalid'

    $invalidProfile = Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.production.invalid.json'
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
    Assert-Throws {
        Test-Cp6P09Profile -RepositoryRoot $repositoryRoot -ProfilePath $invalidProfile -DockerCommand $fakeDocker
    } 'Profile|profile|contract|invalid'
    Assert-True (-not (Test-Path -LiteralPath $fakeLog)) 'Invalid Profile reached Docker.'

    $notRunArtifacts = Join-Path $repositoryRoot 'artifacts\p09-rehearsal'
    $beforeEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    $notRun = Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand (Join-Path $testRoot 'missing-docker') -SkipDotnetPreflight
    Assert-Equal 'NotRun' $notRun.Status 'Genuinely absent Docker must produce NotRun.'
    $afterEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    Assert-Equal $beforeEvidence $afterEvidence 'NotRun wrote rehearsal evidence.'

    Assert-Throws {
        New-Cp6P09RunLayout -RepositoryRoot $repositoryRoot -ArtifactsRoot (Join-Path $testRoot 'outside-artifacts')
    } 'artifact|outside|contained'

    $populationDirectory = Join-Path $testRoot 'population target'
    [IO.Directory]::CreateDirectory($populationDirectory) | Out-Null
    $populationArgs = Get-Cp6P09OwnedFileDockerArguments -ProjectName 'cp6-p09-abcdef0123456789' -ComposeFile (Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml') -Directory $populationDirectory -FileName 'secrets.json' -User '65532:65532'
    Assert-Equal @(
        'compose','--project-name','cp6-p09-abcdef0123456789','--file',(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'),
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--user','65532:65532','--volume',("${populationDirectory}:/out"),
        '--entrypoint','/bin/sh','kafka-admin','-c',"umask 077; cat > '/out/secrets.json'; chmod 0600 '/out/secrets.json'"
    ) $populationArgs 'Target-UID STDIN population command drifted from canonical Compose ordering.'
    Assert-True (($populationArgs -join ' ') -notmatch '(?i)password|token|secret-value') 'Population argv contains secret material.'
    Assert-True ($moduleText -match "(?s)function Assert-Cp6P09TargetReadableFile.+?'--profile','provision','run','--no-TTY','--rm','--no-deps','--user'") 'Target-UID readability probes must disable Compose TTY allocation.'
    Assert-True ($moduleText.Contains('$command = @(''--profile'',''provision'',''run'',''--no-TTY'',''--rm'',''--no-deps'',''--entrypoint''')) 'Kafka one-off tools must disable Compose TTY allocation.'
    Assert-True ($moduleText -match "(?s)Invoke-Cp6P09RuntimeMatrix.+?@\('up','--detach','--build','--wait','--wait-timeout','120','receiver','receiver-dapr'\).+?@\('up','--detach','--build','--wait','--wait-timeout','120','publisher','publisher-dapr'\)") 'Runtime must start receiver and sidecar before publisher and sidecar.'

    $validInvocationTrace = [pscustomobject]@{
        invocationTraceId='44444444444444444444444444444444'; invokerSpanId='5555555555555555'
        invokedSpanId='6666666666666666'; invokedParentSpanId='5555555555555555'
    }
    $validDeliveryTrace = [pscustomobject]@{
        traceId='11111111111111111111111111111111'; publisherSpanId='2222222222222222'
        receiverSpanId='3333333333333333'; receiverParentSpanId='2222222222222222'
    }
    Assert-Cp6P09TraceTopology -Invocation $validInvocationTrace -Delivery $validDeliveryTrace
    $invalidInvocationTrace = $validInvocationTrace.PSObject.Copy()
    $invalidInvocationTrace.invokedParentSpanId = $invalidInvocationTrace.invokedSpanId
    Assert-Throws { Assert-Cp6P09TraceTopology -Invocation $invalidInvocationTrace -Delivery $validDeliveryTrace } 'invoke-positive'
    $invalidDeliveryTrace = $validDeliveryTrace.PSObject.Copy()
    $invalidDeliveryTrace.receiverParentSpanId = $invalidDeliveryTrace.receiverSpanId
    Assert-Throws { Assert-Cp6P09TraceTopology -Invocation $validInvocationTrace -Delivery $invalidDeliveryTrace } 'pubsub-positive'

    foreach ($stableFailure in @('runtime-start','publisher-health','invoke-positive','pubsub-positive','direct-kafka-denied','principal-denied','appid-scope-denied','foreign-topic-denied')) {
        Assert-Equal $stableFailure (Get-Cp6P09StableFailureId -Candidate $stableFailure -Fallback 'runtime-matrix') 'Stable runtime failure id was lost.'
    }
    Assert-Equal 'runtime-matrix' (Get-Cp6P09StableFailureId -Candidate 'password=do-not-log C:\private\file' -Fallback 'runtime-matrix') 'Unsafe exception detail escaped the stable failure allowlist.'

    $head = (& git -C $repositoryRoot rev-parse HEAD).Trim()
    $auditPaths = @(
        '.gitignore','contracts/p09','deploy/p09/compose','src/CP6.Platform.Deployment','eng/p09',
        'eng/run-p09-compose-rehearsal.ps1','tests/p09','tests/CP6.Platform.DeploymentTests/P09Validator',
        'tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj',
        'tests/CP6.Platform.DeploymentTests/P09FixtureRuntimeTests.cs','tests/CP6.Platform.P09Fixture'
    )
    $auditStatus = (& git -C $repositoryRoot status --porcelain=v1 -- @auditPaths) -join "`n"
    if ([string]::IsNullOrWhiteSpace($auditStatus)) {
        Assert-Cp6P09ExpectedGitState -RepositoryRoot $repositoryRoot -ExpectedGitSha $head
    }
    else {
        Assert-Throws { Assert-Cp6P09ExpectedGitState -RepositoryRoot $repositoryRoot -ExpectedGitSha $head } 'dirty|canonical'
    }
    Assert-Throws { Assert-Cp6P09ExpectedGitState -RepositoryRoot $repositoryRoot -ExpectedGitSha ('A' * 40) } '40|lower|SHA'
}
finally {
    Remove-Item Env:CP6_P09_FAKE_DOCKER_LOG -ErrorAction SilentlyContinue
    Remove-Item Env:CP6_P09_FAKE_DOCKER_RESPONSES -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}

Write-Host 'P09 compose rehearsal script tests passed.'
