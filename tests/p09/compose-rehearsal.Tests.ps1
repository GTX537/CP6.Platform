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
$module = Get-Module P09Rehearsal

$runnerText = [IO.File]::ReadAllText($runnerPath, [Text.Encoding]::UTF8)
$moduleText = [IO.File]::ReadAllText($modulePath, [Text.Encoding]::UTF8)
Assert-True ($runnerText -match '(?s)param\(\s*\[string\]\$ProfilePath\s*=\s*"contracts/p09/examples/non-production-runtime-profile.valid.json",\s*\[string\]\$ArtifactsRoot\s*=\s*"artifacts/p09-rehearsal",\s*\[string\]\$ExpectedGitSha,\s*\[switch\]\$KeepFailedArtifacts\s*\)') 'Runner parameters drifted from the approved interface.'
    Assert-True ($moduleText -notmatch '(?i)[A-Za-z]:[\\/]Users[\\/]') 'Runner module contains a machine-specific user path.'
    Assert-True ($moduleText.Contains('$actualCompose.Equals($compose, (Get-Cp6P09PathComparison))')) 'Teardown Compose-label validation is not platform case-sensitive.'

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("cp6-p09-tests-" + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($testRoot) | Out-Null
try {
    foreach ($supported in @('2.36.0', 'v2.36.0', '2.36.0-desktop.1', '2.40.3', '5.1.1', "2.36.0`n", "v2.40.3`r`n")) {
        Assert-True (Test-Cp6P09SupportedComposeVersion -VersionOutput $supported) "Expected Compose version '$supported' to be supported."
    }
    foreach ($unsupported in @($null, '', '2.35.9', 'v2.35.9', '1.99.99', '2.36', 'garbage', '2.36.0 unexpected text', '2147483648.36.0', '2.2147483648.0', '2.36.2147483648')) {
        Assert-True (-not (Test-Cp6P09SupportedComposeVersion -VersionOutput $unsupported)) "Expected Compose version '$unsupported' to be unsupported."
    }

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
    $readinessProperties = & $module {
        New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password 'redacted-test-value' -TimeoutMilliseconds 5000
    }
    $provisionerProperties = & $module {
        New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password 'redacted-test-value' -TimeoutMilliseconds 30000
    }
    foreach ($properties in @($readinessProperties, $provisionerProperties)) {
        $normalizedProperties = $properties.Replace("`r`n", "`n")
        Assert-True ($normalizedProperties.Contains('security.protocol=SASL_PLAINTEXT' + "`n" + 'sasl.mechanism=PLAIN')) 'Kafka client properties changed fixed SASL lines.'
        Assert-True ($normalizedProperties.Contains('sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="cp6-p09-provisioner" password="redacted-test-value";')) 'Kafka client properties changed the provisioner SASL configuration.'
        Assert-Equal @('redacted-test-value') @([regex]::Matches($normalizedProperties, 'password="(?<value>[^"]*)"') | ForEach-Object { $_.Groups['value'].Value }) 'Kafka client properties leaked a password beyond the supplied redacted test value.'
    }
    $normalizedReadinessProperties = $readinessProperties.Replace("`r`n", "`n")
    $normalizedProvisionerProperties = $provisionerProperties.Replace("`r`n", "`n")
    Assert-True ($normalizedReadinessProperties.Contains('request.timeout.ms=5000' + "`n" + 'default.api.timeout.ms=5000')) 'Readiness client properties do not retain the adjacent five-second Kafka timeouts.'
    Assert-True ($normalizedProvisionerProperties.Contains('request.timeout.ms=30000' + "`n" + 'default.api.timeout.ms=30000')) 'Provisioner client properties do not retain the adjacent thirty-second Kafka timeouts.'
    $readinessClientSource = '(?s)@\(\s*''kafka/clients''\s*,\s*''readiness\.properties''\s*,\s*''1000:1000''\s*,\s*\(New-Cp6P09ClientProperties\s+-Username\s+''cp6-p09-provisioner''\s+-Password\s+\$Credentials\.Provisioner\s+-TimeoutMilliseconds\s+5000\)\s*\)'
    $provisionerClientSource = '(?s)@\(\s*''kafka/clients''\s*,\s*''provisioner\.properties''\s*,\s*''1000:1000''\s*,\s*\(New-Cp6P09ClientProperties\s+-Username\s+''cp6-p09-provisioner''\s+-Password\s+\$Credentials\.Provisioner\s+-TimeoutMilliseconds\s+30000\)\s*\)'
    Assert-True ($moduleText -match $readinessClientSource) 'Runtime population does not generate the provisioner readiness client file with a five-second deadline.'
    Assert-True ($moduleText -match $provisionerClientSource) 'Runtime population does not generate the provisioner client file with a thirty-second deadline.'

    $readinessLog = Join-Path $testRoot 'readiness.jsonl'
    $readinessResponses = Join-Path $testRoot 'readiness-responses.jsonl'
    @(
        @{ exitCode = 1; stdout = ''; stderr = 'broker-not-ready' }
        @{ exitCode = 0; stdout = ''; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $readinessResponses -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_LOG = $readinessLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $readinessResponses
    $readinessContext = [pscustomobject]@{
        RepositoryRoot=$repositoryRoot; ProjectName='cp6-p09-abcdef0123456789'
        ComposeFile=(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'); DockerCommand=$fakeDocker
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=$testRoot; CP6_P09_CLUSTER_ID='MkU3OEVBNTcwNTJENDM2Qk' }
    }
    Wait-Cp6P09KafkaDataPlane -Context $readinessContext -Deadline ([DateTimeOffset]::UtcNow.AddSeconds(10))
    $readinessCalls = @(Get-Content -LiteralPath $readinessLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $readinessCalls.Count 'Kafka data-plane readiness did not retry a bounded first failure.'
    foreach ($call in $readinessCalls) {
        Assert-Equal @(
            'compose','--project-name','cp6-p09-abcdef0123456789','--file',(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'),
            '--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint','/opt/kafka/bin/kafka-topics.sh','kafka-admin',
            '--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/readiness.properties','--list'
        ) @($call.argv) 'Kafka data-plane readiness command drifted.'
    }
    $env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''

    $aclBatchArgs = Get-Cp6P09AclBatchDockerArguments `
        -ProjectName 'cp6-p09-abcdef0123456789' `
        -ComposeFile (Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml')
    Assert-Equal @(
        'compose','--project-name','cp6-p09-abcdef0123456789','--file',(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'),
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint','/bin/sh','kafka-admin','-c'
    ) @($aclBatchArgs[0..14]) 'ACL batch command drifted from exact canonical Compose ordering.'
    $aclBatchShell = [string]$aclBatchArgs[15]
    Assert-Equal 9 ([regex]::Matches($aclBatchShell,'/opt/kafka/bin/kafka-acls\.sh')).Count 'ACL batch does not contain exactly nine fixed add commands.'
    foreach ($ordinal in 1..9) {
        Assert-True ($aclBatchShell.Contains(('|| exit {0}' -f (10 + $ordinal)))) "ACL batch does not map tuple $ordinal to a fixed exit ordinal."
        Assert-Equal ('acl-add-first-{0:d2}' -f $ordinal) (Get-Cp6P09AclBatchFailureId -Phase first -ExitCode (10 + $ordinal)) 'First-pass ACL exit mapping drifted.'
        Assert-Equal ('acl-add-replay-{0:d2}' -f $ordinal) (Get-Cp6P09AclBatchFailureId -Phase replay -ExitCode (10 + $ordinal)) 'Replay ACL exit mapping drifted.'
    }
    Assert-Equal 'timeout' (Get-Cp6P09KafkaFailureCategory -StandardOutput '' -StandardError 'org.apache.kafka.common.errors.TimeoutException: Timed out waiting for a node assignment') 'Kafka timeout failure did not map to the closed diagnostic category.'
    Assert-Equal 'authorization' (Get-Cp6P09KafkaFailureCategory -StandardOutput 'TOPIC_AUTHORIZATION_FAILED' -StandardError '') 'Kafka authorization failure did not map to the closed diagnostic category.'
    Assert-Equal 'disconnected' (Get-Cp6P09KafkaFailureCategory -StandardOutput '' -StandardError 'Node 1 disconnected before response') 'Kafka disconnect failure did not map to the closed diagnostic category.'
    Assert-Equal 'metadata' (Get-Cp6P09KafkaFailureCategory -StandardOutput '' -StandardError 'UnknownTopicOrPartitionException') 'Kafka metadata failure did not map to the closed diagnostic category.'
    Assert-Equal 'resource' (Get-Cp6P09KafkaFailureCategory -StandardOutput '' -StandardError 'unable to create native thread') 'Kafka resource failure did not map to the closed diagnostic category.'
    Assert-Equal 'unknown' (Get-Cp6P09KafkaFailureCategory -StandardOutput 'arbitrary bounded text' -StandardError '') 'Unknown Kafka output escaped the closed diagnostic category.'
    Assert-True ($aclBatchShell -notmatch '(?i)password|token|secret') 'ACL batch shell contains secret material.'
    foreach ($fragment in @(
        "'User:cp6-p09-probe-publisher' '--operation' 'Write' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-probe-publisher' '--operation' 'Describe' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-probe-receiver' '--operation' 'Read' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-probe-receiver' '--operation' 'Describe' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-probe-receiver' '--operation' 'Read' '--group' 'cp6-p09-probe-receiver-v1'",
        "'User:cp6-p09-provisioner' '--operation' 'Create' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-provisioner' '--operation' 'Alter' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-provisioner' '--operation' 'Describe' '--topic' 'cp6.platform.deployment-probe.v1'",
        "'User:cp6-p09-provisioner' '--operation' 'Describe' '--cluster'"
    )) {
        Assert-True ($aclBatchShell.Contains($fragment)) "ACL batch omitted exact tuple fragment: $fragment"
    }
    $aclBatchLog = Join-Path $testRoot 'acl-batch.jsonl'
    $env:CP6_P09_FAKE_DOCKER_LOG = $aclBatchLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
    $aclBatchContext = [pscustomobject]@{
        RepositoryRoot=$repositoryRoot; ProjectName='cp6-p09-abcdef0123456789'
        ComposeFile=(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'); DockerCommand=$fakeDocker
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=$testRoot; CP6_P09_CLUSTER_ID='MkU3OEVBNTcwNTJENDM2Qk' }
        ProvisionFailureId='provision-first'; ProvisionFailureCategory=$null
    }
    $module = Get-Module P09Rehearsal
    & $module { param($context) Invoke-Cp6P09AclBatch -Context $context -Phase first; Invoke-Cp6P09AclBatch -Context $context -Phase replay } $aclBatchContext
    $aclBatchCalls = @(Get-Content -LiteralPath $aclBatchLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $aclBatchCalls.Count 'Provision must use exactly one ACL batch for first pass and one for replay.'
    Assert-Equal @($aclBatchCalls[0].argv) @($aclBatchCalls[1].argv) 'First and replay ACL batch commands differ.'
    Assert-Equal @(0,0) @($aclBatchCalls.stdinBytes) 'ACL batch unexpectedly received secret STDIN.'

    $aclFailureResponses = Join-Path $testRoot 'acl-failure-responses.jsonl'
    @{ exitCode = 13; stdout = ''; stderr = 'org.apache.kafka.common.errors.TimeoutException: Timed out waiting for a node assignment' } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $aclFailureResponses -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $aclFailureResponses
    Remove-Item -LiteralPath "$aclBatchLog.index" -Force -ErrorAction SilentlyContinue
    $aclBatchContext.ProvisionFailureCategory = $null
    Assert-Throws {
        & $module { param($context) Invoke-Cp6P09AclBatch -Context $context -Phase first } $aclBatchContext
    } 'acl-add-first-03'
    Assert-Equal 'timeout' $aclBatchContext.ProvisionFailureCategory 'ACL failure did not retain only its closed diagnostic category.'
    $env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''

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

    $unsupportedComposeResponses = Join-Path $testRoot 'unsupported-compose-responses.jsonl'
    @(
        @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
        @{ exitCode = 0; stdout = '2.35.9'; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $unsupportedComposeResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $unsupportedComposeResponses
    $beforeUnsupportedEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    $unsupportedCompose = Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand $fakeDocker -SkipDotnetPreflight
    Assert-Equal 'NotRun' $unsupportedCompose.Status 'Unsupported Compose version must fail closed.'
    Assert-Equal 'unsupported-compose-version' $unsupportedCompose.Reason 'Unsupported Compose version did not return the stable closed reason.'
    $unsupportedComposeCalls = @(Get-Content -LiteralPath $fakeLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $unsupportedComposeCalls.Count 'Unsupported Compose version should issue exactly two Docker calls.'
    Assert-Equal @('version','--format','{{.Server.Version}}') @($unsupportedComposeCalls[0].argv) 'Unsupported Compose version first Docker call drifted.'
    Assert-Equal @('compose','version','--short') @($unsupportedComposeCalls[1].argv) 'Unsupported Compose version second Docker call drifted.'
    $afterUnsupportedEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    Assert-Equal $beforeUnsupportedEvidence $afterUnsupportedEvidence 'Unsupported Compose version wrote rehearsal evidence.'
    $unsupportedArtifactDirectory = Join-Path $notRunArtifacts $unsupportedCompose.RunId
    Assert-True (-not (Test-Path -LiteralPath $unsupportedArtifactDirectory)) 'Unsupported Compose version created a per-run artifact directory.'
    $unsupportedComposeTempRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p09-' + $unsupportedCompose.RunId)
    Assert-True (-not (Test-Path -LiteralPath $unsupportedComposeTempRoot)) 'Unsupported Compose version created a runtime temp root.'

    $supportedComposeConfigFailureResponses = Join-Path $testRoot 'supported-compose-config-failure-responses.jsonl'
    @(
        @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
        @{ exitCode = 0; stdout = "2.36.0`r`n"; stderr = '' }
        @{ exitCode = 1; stdout = ''; stderr = 'config validation failed' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $supportedComposeConfigFailureResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $supportedComposeConfigFailureResponses
    Assert-Throws {
        Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand $fakeDocker -SkipDotnetPreflight
    } 'compose-contract'
    $supportedComposeConfigFailureCalls = @(Get-Content -LiteralPath $fakeLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 3 $supportedComposeConfigFailureCalls.Count 'Supported Compose probe should reach the config preflight as a third Docker call.'
    Assert-Equal @('version','--format','{{.Server.Version}}') @($supportedComposeConfigFailureCalls[0].argv) 'Supported Compose probe first Docker call drifted.'
    Assert-Equal @('compose','version','--short') @($supportedComposeConfigFailureCalls[1].argv) 'Supported Compose probe second Docker call drifted.'
    Assert-Equal 11 @($supportedComposeConfigFailureCalls[2].argv).Count 'Supported Compose preflight third Docker call did not keep the canonical arity.'
    Assert-Equal 'compose' $supportedComposeConfigFailureCalls[2].argv[0] 'Supported Compose preflight third Docker call lost the compose verb.'
    Assert-Equal '--project-name' $supportedComposeConfigFailureCalls[2].argv[1] 'Supported Compose preflight third Docker call lost the project-name flag.'
    Assert-True ([string]$supportedComposeConfigFailureCalls[2].argv[2] -cmatch '^cp6-p09-[a-f0-9]{16}$') 'Supported Compose preflight third Docker call project name drifted.'
    Assert-Equal '--file' $supportedComposeConfigFailureCalls[2].argv[3] 'Supported Compose preflight third Docker call lost the compose file flag.'
    Assert-Equal (Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml') $supportedComposeConfigFailureCalls[2].argv[4] 'Supported Compose preflight third Docker call lost the canonical compose path.'
    Assert-Equal @('--profile','negative','--profile','provision','config','--quiet') @($supportedComposeConfigFailureCalls[2].argv[5..10]) 'Supported Compose preflight third Docker call drifted from the canonical config probe.'

    $composeProbeFailureResponses = Join-Path $testRoot 'compose-probe-failure-responses.jsonl'
    @(
        @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
        @{ exitCode = 1; stdout = ''; stderr = 'compose version failed' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $composeProbeFailureResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $composeProbeFailureResponses
    $beforeProbeFailureEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    $composeProbeFailure = Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand $fakeDocker -SkipDotnetPreflight
    Assert-Equal 'NotRun' $composeProbeFailure.Status 'Compose probe failure must fail closed.'
    Assert-Equal 'unsupported-compose-version' $composeProbeFailure.Reason 'Compose probe failure did not return the stable closed reason.'
    $composeProbeFailureCalls = @(Get-Content -LiteralPath $fakeLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $composeProbeFailureCalls.Count 'Compose probe failure should issue exactly two Docker calls.'
    Assert-Equal @('version','--format','{{.Server.Version}}') @($composeProbeFailureCalls[0].argv) 'Compose probe failure first Docker call drifted.'
    Assert-Equal @('compose','version','--short') @($composeProbeFailureCalls[1].argv) 'Compose probe failure second Docker call drifted.'
    $afterProbeFailureEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    Assert-Equal $beforeProbeFailureEvidence $afterProbeFailureEvidence 'Compose probe failure wrote rehearsal evidence.'
    $composeProbeFailureArtifactDirectory = Join-Path $notRunArtifacts $composeProbeFailure.RunId
    Assert-True (-not (Test-Path -LiteralPath $composeProbeFailureArtifactDirectory)) 'Compose probe failure created a per-run artifact directory.'
    $composeProbeFailureTempRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p09-' + $composeProbeFailure.RunId)
    Assert-True (-not (Test-Path -LiteralPath $composeProbeFailureTempRoot)) 'Compose probe failure created a runtime temp root.'

    $malformedComposeResponses = Join-Path $testRoot 'malformed-compose-responses.jsonl'
    @(
        @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
        @{ exitCode = 0; stdout = 'garbage'; stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $malformedComposeResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $malformedComposeResponses
    $beforeMalformedEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    $malformedCompose = Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand $fakeDocker -SkipDotnetPreflight
    Assert-Equal 'NotRun' $malformedCompose.Status 'Malformed Compose version must fail closed.'
    Assert-Equal 'unsupported-compose-version' $malformedCompose.Reason 'Malformed Compose version did not return the stable closed reason.'
    $malformedComposeCalls = @(Get-Content -LiteralPath $fakeLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $malformedComposeCalls.Count 'Malformed Compose version should issue exactly two Docker calls.'
    Assert-Equal @('version','--format','{{.Server.Version}}') @($malformedComposeCalls[0].argv) 'Malformed Compose version first Docker call drifted.'
    Assert-Equal @('compose','version','--short') @($malformedComposeCalls[1].argv) 'Malformed Compose version second Docker call drifted.'
    $afterMalformedEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    Assert-Equal $beforeMalformedEvidence $afterMalformedEvidence 'Malformed Compose version wrote rehearsal evidence.'
    $malformedComposeArtifactDirectory = Join-Path $notRunArtifacts $malformedCompose.RunId
    Assert-True (-not (Test-Path -LiteralPath $malformedComposeArtifactDirectory)) 'Malformed Compose version created a per-run artifact directory.'
    $malformedComposeTempRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p09-' + $malformedCompose.RunId)
    Assert-True (-not (Test-Path -LiteralPath $malformedComposeTempRoot)) 'Malformed Compose version created a runtime temp root.'

    $composeProbeExceptionResponses = Join-Path $testRoot 'compose-probe-exception-responses.jsonl'
    @(
        @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
        @{ exitCode = 0; stdout = ('x' * 70000); stderr = '' }
    ) | ForEach-Object { $_ | ConvertTo-Json -Compress } | Set-Content -LiteralPath $composeProbeExceptionResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath $fakeLog -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath "$fakeLog.index" -Force -ErrorAction SilentlyContinue
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $composeProbeExceptionResponses
    $beforeComposeProbeExceptionEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    $composeProbeException = Invoke-Cp6P09Rehearsal -RepositoryRoot $repositoryRoot -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') -ArtifactsRoot $notRunArtifacts -DockerCommand $fakeDocker -SkipDotnetPreflight
    Assert-Equal 'NotRun' $composeProbeException.Status 'Exceptional Compose probe must fail closed.'
    Assert-Equal 'unsupported-compose-version' $composeProbeException.Reason 'Exceptional Compose probe did not return the stable closed reason.'
    $composeProbeExceptionCalls = @(Get-Content -LiteralPath $fakeLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 2 $composeProbeExceptionCalls.Count 'Exceptional Compose probe should issue exactly two Docker calls.'
    Assert-Equal @('version','--format','{{.Server.Version}}') @($composeProbeExceptionCalls[0].argv) 'Exceptional Compose probe first Docker call drifted.'
    Assert-Equal @('compose','version','--short') @($composeProbeExceptionCalls[1].argv) 'Exceptional Compose probe second Docker call drifted.'
    $afterComposeProbeExceptionEvidence = @(Get-ChildItem -LiteralPath $notRunArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
    Assert-Equal $beforeComposeProbeExceptionEvidence $afterComposeProbeExceptionEvidence 'Exceptional Compose probe wrote rehearsal evidence.'
    $composeProbeExceptionArtifactDirectory = Join-Path $notRunArtifacts $composeProbeException.RunId
    Assert-True (-not (Test-Path -LiteralPath $composeProbeExceptionArtifactDirectory)) 'Exceptional Compose probe created a per-run artifact directory.'
    $composeProbeExceptionTempRoot = Join-Path ([IO.Path]::GetTempPath()) ('cp6-p09-' + $composeProbeException.RunId)
    Assert-True (-not (Test-Path -LiteralPath $composeProbeExceptionTempRoot)) 'Exceptional Compose probe created a runtime temp root.'
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''

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
    $readabilityArgs = Get-Cp6P09ReadabilityDockerArguments `
        -ProjectName 'cp6-p09-abcdef0123456789' `
        -ComposeFile (Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml') `
        -Directory $populationDirectory `
        -FileNames @('secret-store.yaml','subscription.yaml') `
        -User '65532:65532'
    Assert-Equal @(
        'compose','--project-name','cp6-p09-abcdef0123456789','--file',(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'),
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--user','65532:65532','--volume',("${populationDirectory}:/input:ro"),
        '--entrypoint','/bin/sh','kafka-admin','-c',"test -r '/input/secret-store.yaml' && test -r '/input/subscription.yaml'"
    ) $readabilityArgs 'Target-UID directory-group readability command drifted.'
    Assert-True ($moduleText -match "(?s)function Get-Cp6P09ReadabilityDockerArguments.+?'--profile','provision','run','--no-TTY','--rm','--no-deps','--user'") 'Target-UID readability probes must disable Compose TTY allocation.'
    Assert-True ($moduleText.Contains("`$Context.PopulationFailureId = 'runtime-readability'")) 'Generic readability timeouts are not mapped to the stable runtime-readability id.'
    $populationLog = Join-Path $testRoot 'population.jsonl'
    $env:CP6_P09_FAKE_DOCKER_LOG = $populationLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
    $populationContext = [pscustomobject]@{
        RepositoryRoot=$repositoryRoot; ProjectName='cp6-p09-abcdef0123456789'
        ComposeFile=(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'); DockerCommand=$fakeDocker
        RuntimeRoot=(Join-Path $testRoot 'runtime-population')
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=(Join-Path $testRoot 'runtime-population'); CP6_P09_CLUSTER_ID='MkU3OEVBNTcwNTJENDM2Qk' }
        PopulationFailureId='runtime-population'
    }
    & $module { param($context,$values) Initialize-Cp6P09RuntimeFiles -Context $context -Credentials $values } $populationContext $credentials
    $populationCalls = @(Get-Content -LiteralPath $populationLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 26 $populationCalls.Count 'Runtime ownership preflight must use 17 writes plus 9 directory-group readability calls.'
    Assert-Equal 17 @($populationCalls | Where-Object { (@($_.argv) -join ' ') -match ':/out(?:\s|$)' }).Count 'Target-UID STDIN write call count drifted.'
    Assert-Equal 9 @($populationCalls | Where-Object { (@($_.argv) -join ' ') -match ':/input:ro(?:\s|$)' }).Count 'Directory-group readability call count drifted.'
    $env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
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

    $retryState = [pscustomobject]@{ Attempts=0 }
    $retryResult = Invoke-Cp6P09BoundedRetry -Deadline ([DateTimeOffset]::UtcNow.AddSeconds(5)) -FailureId 'invoke-positive' -Action {
        $retryState.Attempts++
        if ($retryState.Attempts -eq 1) { throw 'transient-status' }
        return 'ready'
    }
    Assert-Equal 'ready' $retryResult 'Bounded invocation retry did not return the first successful result.'
    Assert-Equal 2 $retryState.Attempts 'Bounded invocation retry did not retry exactly once.'
    Assert-Throws {
        Invoke-Cp6P09BoundedRetry -Deadline ([DateTimeOffset]::UtcNow.AddMilliseconds(-1)) -FailureId 'invoke-positive' -Action { 'unexpected' }
    } 'invoke-positive'
    Assert-True ($moduleText.Contains('$matrixDeadline = [DateTimeOffset]::UtcNow.AddSeconds(60)')) 'Runtime matrix does not create a 60-second shared deadline.'
    Assert-True ($moduleText.Contains("Invoke-Cp6P09BoundedRetry -Deadline `$matrixDeadline -FailureId 'invoke-positive'")) 'Invocation does not use the shared matrix deadline.'
    Assert-True ($moduleText.Contains('} while ([DateTimeOffset]::UtcNow -lt $matrixDeadline)')) 'Publish polling does not reuse the shared matrix deadline.'

    $diagnosticSpec = Get-Cp6P09DaprDiagnosticProcessSpec `
        -ProjectName 'cp6-p09-abcdef0123456789' `
        -ComposeFile (Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml')
    Assert-Equal 15 $diagnosticSpec.TimeoutSeconds 'Dapr diagnostic outer timeout drifted.'
    Assert-Equal 4096 $diagnosticSpec.MaximumOutputBytes 'Dapr diagnostic output bound drifted.'
    Assert-Equal @(
        'compose','--project-name','cp6-p09-abcdef0123456789','--file',(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'),
        '--profile','provision','run','--no-TTY','--rm','--no-deps','--entrypoint','/bin/bash','kafka-admin','-c'
    ) @($diagnosticSpec.Arguments[0..14]) 'Dapr diagnostic escaped the exact canonical kafka-admin one-off.'
    $diagnosticShell = [string]$diagnosticSpec.Arguments[15]
    Assert-True ($diagnosticShell.Contains('/dev/tcp/publisher-dapr/3500')) 'Dapr diagnostic endpoint drifted.'
    Assert-True ($diagnosticShell.Contains('POST /v1.0/invoke/cp6-p09-probe-receiver/method/invoked HTTP/1.1')) 'Dapr diagnostic method drifted.'
    Assert-True ($diagnosticShell.Contains('Content-Length: 34')) 'Dapr diagnostic body length is not compile-time fixed.'
    Assert-True ($diagnosticShell.Contains('{"correlationId":"p09-diagnostic"}')) 'Dapr diagnostic body drifted.'
    Assert-True ($diagnosticShell.Contains('timeout 10 head -c 3072')) 'Dapr diagnostic inner read is not bounded.'
    Assert-True ($diagnosticShell -notmatch '\$|(?i)password|token|secret') 'Dapr diagnostic contains interpolation or secret material.'

    $diagnosticLog = Join-Path $testRoot 'dapr-diagnostic.jsonl'
    $diagnosticResponses = Join-Path $testRoot 'dapr-diagnostic-responses.jsonl'
    $diagnosticPayload = "HTTP/1.1 500 Internal Server Error`r`nContent-Type: application/json`r`n`r`n{`"errorCode`":`"ERR_DIRECT_INVOKE`",`"message`":`"rpc error: code = Unavailable desc = connection error`"}"
    @{ exitCode=0; stdout=$diagnosticPayload; stderr='' } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $diagnosticResponses -Encoding utf8NoBOM
    $env:CP6_P09_FAKE_DOCKER_LOG = $diagnosticLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = $diagnosticResponses
    $diagnosticContext = [pscustomobject]@{
        RepositoryRoot=$repositoryRoot; ProjectName='cp6-p09-abcdef0123456789'
        ComposeFile=(Join-Path $repositoryRoot 'deploy\p09\compose\compose.yaml'); DockerCommand=$fakeDocker
        Environment=[ordered]@{ CP6_P09_RUNTIME_ROOT=$testRoot; CP6_P09_CLUSTER_ID='MkU3OEVBNTcwNTJENDM2Qk' }
    }
    Assert-Equal 'target-unavailable' (Invoke-Cp6P09DaprDiagnostic -Context $diagnosticContext) 'Dapr diagnostic did not classify the fixed Unavailable response.'
    $diagnosticCalls = @(Get-Content -LiteralPath $diagnosticLog | ForEach-Object { $_ | ConvertFrom-Json })
    Assert-Equal 1 $diagnosticCalls.Count 'Dapr diagnostic issued more than its single fixed one-off.'
    Assert-Equal @($diagnosticSpec.Arguments) @($diagnosticCalls[0].argv) 'Dapr diagnostic fake argv drifted.'
    Assert-Equal 0 $diagnosticCalls[0].stdinBytes 'Dapr diagnostic unexpectedly received secret STDIN.'

    @{ exitCode=124; stdout=$diagnosticPayload; stderr='' } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $diagnosticResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath "$diagnosticLog.index" -Force -ErrorAction SilentlyContinue
    Assert-Equal 'target-unavailable' (Invoke-Cp6P09DaprDiagnostic -Context $diagnosticContext) 'A valid bounded HTTP response was discarded only because the inner reader reached its timeout.'

    $missingAppPayload = "HTTP/1.1 500 Internal Server Error`r`nContent-Type: application/json`r`n`r`n{`"errorCode`":`"ERR_DIRECT_INVOKE`",`"message`":`"failed to invoke, id: cp6-p09-probe-receiver, err: couldn't find service: cp6-p09-probe-receiver`"}"
    @{ exitCode=124; stdout=$missingAppPayload; stderr='' } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $diagnosticResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath "$diagnosticLog.index" -Force -ErrorAction SilentlyContinue
    Assert-Equal 'service-discovery-unavailable' (Invoke-Cp6P09DaprDiagnostic -Context $diagnosticContext) 'The exact Dapr self-hosted missing-app response escaped its closed category.'

    $addressTimeoutPayload = "HTTP/1.1 500 Internal Server Error`r`nContent-Type: application/json`r`n`r`n{`"errorCode`":`"ERR_DIRECT_INVOKE`",`"message`":`"failed to invoke, id: cp6-p09-probe-receiver, err: timeout waiting for address for app id cp6-p09-probe-receiver`"}"
    @{ exitCode=124; stdout=$addressTimeoutPayload; stderr='' } |
        ConvertTo-Json -Compress | Set-Content -LiteralPath $diagnosticResponses -Encoding utf8NoBOM
    Remove-Item -LiteralPath "$diagnosticLog.index" -Force -ErrorAction SilentlyContinue
    Assert-Equal 'service-discovery-unavailable' (Invoke-Cp6P09DaprDiagnostic -Context $diagnosticContext) 'The exact Dapr self-hosted address-timeout response escaped its closed category.'

    $invalidDiagnosticResponses = @(
        @{ exitCode=124; stdout=''; stderr='bounded diagnostic failure'; name='missing status' }
        @{ exitCode=124; stdout=($diagnosticPayload + "`r`nHTTP/1.1 500 Internal Server Error"); stderr=''; name='multiple status lines' }
        @{ exitCode=124; stdout="HTTP/1.1 500 Internal Server Error`r`n`r`n{`"errorCode`":`"ERR_DIRECT_INVOKE`""; stderr=''; name='truncated body' }
        @{ exitCode=124; stdout="HTTP/1.1 500 Internal Server Error`r`n`r`n{`"errorCode`":`"ERR_DIRECT_INVOKE`",`"message`":`"unclassified`"}"; stderr=''; name='unknown body' }
        @{ exitCode=124; stdout=($missingAppPayload.Replace('cp6-p09-probe-receiver','cp6-p09-foreign-app')); stderr=''; name='foreign app id' }
        @{ exitCode=124; stdout=($diagnosticPayload + ('x' * (3073 - $diagnosticPayload.Length))); stderr=''; name='oversized stdout' }
        @{ exitCode=1; stdout=$diagnosticPayload; stderr=''; name='unexpected nonzero exit' }
    )
    foreach ($invalidDiagnostic in $invalidDiagnosticResponses) {
        $invalidDiagnostic | Select-Object exitCode,stdout,stderr | ConvertTo-Json -Compress |
            Set-Content -LiteralPath $diagnosticResponses -Encoding utf8NoBOM
        Remove-Item -LiteralPath "$diagnosticLog.index" -Force -ErrorAction SilentlyContinue
        Assert-Equal 'diagnostic-unavailable' (Invoke-Cp6P09DaprDiagnostic -Context $diagnosticContext) "Dapr diagnostic accepted $($invalidDiagnostic.name)."
    }
    Assert-True ($moduleText -match '(?s)catch\s*\{\s*\$Context\.MatrixDiagnosticCategory\s*=\s*Invoke-Cp6P09DaprDiagnostic.+?throw\s*\}') 'Dapr diagnostic is not synchronized after final invoke-positive failure while preserving the original exception.'
    Assert-True ($moduleText.Contains('DiagnosticCategory=$context.MatrixDiagnosticCategory')) 'Runner result does not expose only the closed Dapr diagnostic category.'
    $env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
    $env:CP6_P09_FAKE_DOCKER_RESPONSES = ''

    Assert-Equal 'kafka-health' (Get-Cp6P09StableFailureId -Candidate 'kafka-health' -Fallback 'kafka-start') 'Stable Kafka health failure id was lost.'
    foreach ($stableFailure in @('runtime-start','publisher-health','invoke-positive','pubsub-positive','direct-kafka-denied','principal-denied','appid-scope-denied','foreign-topic-denied')) {
        Assert-Equal $stableFailure (Get-Cp6P09StableFailureId -Candidate $stableFailure -Fallback 'runtime-matrix') 'Stable runtime failure id was lost.'
        Assert-True ($moduleText.Contains("`$Context.MatrixFailureId = '$stableFailure'")) "Runtime matrix does not checkpoint $stableFailure before its side effect."
    }
    foreach ($stableFailure in @('topic-list','publisher-port')) {
        Assert-True ($moduleText.Contains("`$Context.MatrixFailureId = '$stableFailure'")) "Runtime matrix does not checkpoint $stableFailure before its side effect."
    }
    $provisionFailureIds = @('topic-create-first','topic-describe-first','acl-list-first','topic-create-replay','acl-list-replay') +
        @(1..9 | ForEach-Object { 'acl-add-first-{0:d2}' -f $_ }) +
        @(1..9 | ForEach-Object { 'acl-add-replay-{0:d2}' -f $_ })
    foreach ($stableFailure in $provisionFailureIds) {
        Assert-Equal $stableFailure (Get-Cp6P09StableFailureId -Candidate $stableFailure -Fallback 'provision-first') 'Stable provision failure id was lost.'
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
