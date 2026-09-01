# P09 Compose Dapr Runtime NIC Calibration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在不削弱应用/Kafka 网络隔离的前提下，确定化三个 Dapr sidecar 的 Compose 运行接口与默认网关，失败关闭地拒绝旧版 Compose，并消除 Kafka readiness timeout 对正常 provision 的耦合。

**Architecture:** 保留四网络拓扑，只把三个双网卡 sidecar 的 `runtime` 固定为 `eth0/gw_priority=1`、各自应用私网固定为 `eth1/gw_priority=0`。runner 在任何运行副作用前要求 Docker Compose `>=2.36.0`；Kafka readiness 使用独立 5 秒 client 配置，正常 provision 使用 30 秒 client 配置并继续受 120 秒外层 deadline 约束。

**Tech Stack:** Docker Compose v2.36+, Dapr 1.18.2, Apache Kafka 4.3.1, PowerShell 7, .NET 8/C# 12, xUnit, fake-Docker process harness

---

## 范围与执行边界

本计划只实现并验证已批准的 `docs/superpowers/specs/2026-08-31-p09-compose-dapr-runtime-nic-design.md`。它不创建 Kubernetes 资产、不发布包、不修改 CRM、不连接云或生产环境，也不改变 `Implemented / Rehearsal Candidate` 的状态上限。

开始执行前要求：

```powershell
git status --short --branch
git rev-parse HEAD
```

预期：分支为 `codex/p09-nonprod-runtime-implementation`，工作区干净，HEAD 包含已批准的设计校准和本计划。不得清理、覆盖或暂存任何意外文件。

手工运行 .NET 命令时只从环境或 PATH 解析 host：

```powershell
$dotnet = if (-not [string]::IsNullOrWhiteSpace($env:DOTNET_HOST_PATH)) { $env:DOTNET_HOST_PATH } else { 'dotnet' }
```

仓库脚本不得写入用户目录或机器专属 `dotnet.exe` 路径。

## 文件职责映射

| 文件 | 单一职责 |
| --- | --- |
| `tests/p09/compose-rehearsal.Tests.ps1` | fake-Docker runner 合同、Compose 版本边界、Kafka client timeout 与 readiness 命令的脚本级 TDD |
| `eng/p09/P09Rehearsal.psm1` | 版本解析与失败关闭 preflight、临时 Kafka client 配置生成、真实演练 orchestration |
| `tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs` | Compose 文本与规范化 JSON 的精确拓扑、接口、网关和 healthcheck 合同 |
| `deploy/p09/compose/compose.yaml` | 唯一 P09 Compose 运行拓扑；不承载凭据或机器专属值 |

不新增解析库、模板生成器、名称解析服务或网络抽象层。

### Task 1: Fail closed below Docker Compose 2.36.0

**Files:**
- Modify: `tests/p09/compose-rehearsal.Tests.ps1:29-240`
- Modify: `eng/p09/P09Rehearsal.psm1:386-445,1256-1304,1420-1448`

- [ ] **Step 1: Write RED tests for exact version boundaries and zero-side-effect NotRun**

在 `Import-Module $modulePath -Force` 后保存模块对象：

```powershell
$module = Get-Module P09Rehearsal
```

在现有 absent-Docker `NotRun` 测试前加入以下边界断言：

```powershell
foreach ($supported in @('2.36.0', 'v2.36.0', '2.36.0-desktop.1', '2.40.3', '5.1.1')) {
    Assert-True (Test-Cp6P09SupportedComposeVersion -VersionOutput $supported) "Supported Compose version was rejected: $supported"
}
foreach ($unsupported in @($null, '', '2.35.9', 'v2.35.9', '1.99.99', '2.36', 'garbage', '2.36.0 unexpected text')) {
    Assert-True (-not (Test-Cp6P09SupportedComposeVersion -VersionOutput $unsupported)) "Unsupported Compose version was accepted: $unsupported"
}

$unsupportedLog = Join-Path $testRoot 'unsupported-compose.jsonl'
$unsupportedResponses = Join-Path $testRoot 'unsupported-compose-responses.jsonl'
@(
    @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
    @{ exitCode = 0; stdout = '2.35.9'; stderr = '' }
) | ForEach-Object { $_ | ConvertTo-Json -Compress } |
    Set-Content -LiteralPath $unsupportedResponses -Encoding utf8NoBOM
$env:CP6_P09_FAKE_DOCKER_LOG = $unsupportedLog
$env:CP6_P09_FAKE_DOCKER_RESPONSES = $unsupportedResponses
$unsupportedArtifacts = Join-Path $repositoryRoot 'artifacts\p09-rehearsal'
$beforeUnsupportedEvidence = @(Get-ChildItem -LiteralPath $unsupportedArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
$unsupportedResult = Invoke-Cp6P09Rehearsal `
    -RepositoryRoot $repositoryRoot `
    -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') `
    -ArtifactsRoot $unsupportedArtifacts `
    -DockerCommand $fakeDocker `
    -SkipDotnetPreflight
Assert-Equal 'NotRun' $unsupportedResult.Status 'Compose below 2.36.0 must be NotRun.'
Assert-Equal 'unsupported-compose-version' $unsupportedResult.Reason 'Unsupported Compose reason drifted.'
$unsupportedCalls = @(Get-Content -LiteralPath $unsupportedLog | ForEach-Object { $_ | ConvertFrom-Json })
Assert-Equal 2 $unsupportedCalls.Count 'Unsupported Compose reached a Docker side-effecting command.'
Assert-Equal @('version','--format','{{.Server.Version}}') @($unsupportedCalls[0].argv) 'Docker availability preflight drifted.'
Assert-Equal @('compose','version','--short') @($unsupportedCalls[1].argv) 'Compose version preflight drifted.'
$afterUnsupportedEvidence = @(Get-ChildItem -LiteralPath $unsupportedArtifacts -Recurse -Filter 'rehearsal-evidence.v1.json' -ErrorAction SilentlyContinue).Count
Assert-Equal $beforeUnsupportedEvidence $afterUnsupportedEvidence 'Unsupported Compose wrote Passed evidence.'
Assert-True (-not (Test-Path -LiteralPath (Join-Path ([IO.Path]::GetTempPath()) ("cp6-p09-$($unsupportedResult.RunId)")))) 'Unsupported Compose created a runtime root.'

$failedComposeLog = Join-Path $testRoot 'failed-compose-version.jsonl'
$failedComposeResponses = Join-Path $testRoot 'failed-compose-version-responses.jsonl'
@(
    @{ exitCode = 0; stdout = '26.1.4'; stderr = '' }
    @{ exitCode = 1; stdout = ''; stderr = 'compose plugin unavailable' }
) | ForEach-Object { $_ | ConvertTo-Json -Compress } |
    Set-Content -LiteralPath $failedComposeResponses -Encoding utf8NoBOM
$env:CP6_P09_FAKE_DOCKER_LOG = $failedComposeLog
$env:CP6_P09_FAKE_DOCKER_RESPONSES = $failedComposeResponses
$failedComposeResult = Invoke-Cp6P09Rehearsal `
    -RepositoryRoot $repositoryRoot `
    -ProfilePath (Join-Path $repositoryRoot 'contracts\p09\examples\non-production-runtime-profile.valid.json') `
    -ArtifactsRoot $unsupportedArtifacts `
    -DockerCommand $fakeDocker `
    -SkipDotnetPreflight
Assert-Equal 'NotRun' $failedComposeResult.Status 'Unavailable Compose plugin must be NotRun.'
Assert-Equal 'unsupported-compose-version' $failedComposeResult.Reason 'Unavailable Compose plugin escaped the closed reason.'
Assert-Equal 2 @(Get-Content -LiteralPath $failedComposeLog).Count 'Failed Compose version command reached runtime Docker calls.'

$env:CP6_P09_FAKE_DOCKER_LOG = $fakeLog
$env:CP6_P09_FAKE_DOCKER_RESPONSES = ''
```

- [ ] **Step 2: Run the script test and confirm RED**

Run:

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
```

Expected: FAIL because `Test-Cp6P09SupportedComposeVersion` does not exist or because `2.35.9` still passes the current non-empty version check. No real Docker resource is created.

- [ ] **Step 3: Add strict version parsing and wire it into preflight**

在 `Invoke-Cp6P09Compose` 后加入：

```powershell
function Test-Cp6P09SupportedComposeVersion {
    [CmdletBinding()]
    param([AllowNull()][AllowEmptyString()][string]$VersionOutput)

    if ([string]::IsNullOrWhiteSpace($VersionOutput)) { return $false }
    $candidate = $VersionOutput.Trim()
    $match = [regex]::Match(
        $candidate,
        '^v?(?<major>[0-9]+)\.(?<minor>[0-9]+)\.(?<patch>[0-9]+)(?:[-+][0-9A-Za-z.-]+)?$',
        [Text.RegularExpressions.RegexOptions]::CultureInvariant)
    if (-not $match.Success) { return $false }

    try {
        $version = [Version]::new(
            [int]$match.Groups['major'].Value,
            [int]$match.Groups['minor'].Value,
            [int]$match.Groups['patch'].Value)
    }
    catch {
        return $false
    }
    return $version -ge [Version]::new(2, 36, 0)
}
```

把 `Invoke-Cp6P09Rehearsal` 中 Compose 版本判断替换为：

```powershell
$composeVersion = Invoke-Cp6P09DockerCommand -DockerCommand $DockerCommand -WorkingDirectory $repository -Arguments @('compose','version','--short') -TimeoutSeconds 30
if ($composeVersion.ExitCode -ne 0 -or
    -not (Test-Cp6P09SupportedComposeVersion -VersionOutput $composeVersion.StandardOutput)) {
    return [pscustomobject]@{
        Status='NotRun'
        RunId=$layout.RunId
        Reason='unsupported-compose-version'
    }
}
```

在 `Export-ModuleMember` 列表加入：

```powershell
'Test-Cp6P09SupportedComposeVersion',
```

不要改变 Docker executable 缺失时的 `docker-unavailable` 分类，也不要在版本失败路径创建 artifact/runtime 目录。

- [ ] **Step 4: Run focused script gates and confirm GREEN**

Run:

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
```

Expected: both print `PASS`; version cases accept `2.36.0` and higher, reject lower/malformed/failed Compose, and unsupported paths record exactly two fake-Docker calls.

- [ ] **Step 5: Review and commit only the version gate**

```powershell
git diff --check
git diff -- eng/p09/P09Rehearsal.psm1 tests/p09/compose-rehearsal.Tests.ps1
git add -- eng/p09/P09Rehearsal.psm1 tests/p09/compose-rehearsal.Tests.ps1
git commit -m "fix: gate P09 rehearsal on supported Compose"
```

Expected: one auditable commit; no Compose topology or Kafka client timeout change is included.

### Task 2: Freeze deterministic Dapr sidecar NICs without widening connectivity

**Files:**
- Modify: `tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs:115-175,347-380,387-470,729-735,1134-1145`
- Modify: `deploy/p09/compose/compose.yaml:97-127,144-174,192-222`

- [ ] **Step 1: Write static RED tests for text, mutations, and rendered JSON**

把 `ServiceNetworks` 改为基于既有严格 YAML block helper 读取嵌套映射：

```csharp
private static string[] ServiceNetworks(string compose, string service)
{
    var networks = RequiredBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "networks");
    return DirectMapKeys(networks, 6);
}
```

加入以下文本断言 helper：

```csharp
private static void AssertServiceNetworkAttachment(
    string compose,
    string service,
    string network,
    string interfaceName,
    string gatewayPriority)
{
    var networks = RequiredBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "networks");
    var attachment = RequiredBlock(networks, 6, network);
    Assert.Equal(new[] { "gw_priority", "interface_name" }, DirectMapKeys(attachment, 8));
    Assert.Equal(interfaceName, RequiredScalar(attachment, 8, "interface_name"));
    Assert.Equal(gatewayPriority, RequiredScalar(attachment, 8, "gw_priority"));
}

private static void AssertExactDaprNetworkAttachments(string compose)
{
    AssertServiceNetworkAttachment(compose, "publisher-dapr", "runtime", "eth0", "1");
    AssertServiceNetworkAttachment(compose, "publisher-dapr", "publisher-app", "eth1", "0");
    AssertServiceNetworkAttachment(compose, "receiver-dapr", "runtime", "eth0", "1");
    AssertServiceNetworkAttachment(compose, "receiver-dapr", "receiver-app", "eth1", "0");
    AssertServiceNetworkAttachment(compose, "unauthorized-dapr", "runtime", "eth0", "1");
    AssertServiceNetworkAttachment(compose, "unauthorized-dapr", "unauthorized-app", "eth1", "0");
}
```

在 `ComposeText_FreezesTheExactIsolatedTopology` 的网络归属断言后调用：

```csharp
AssertExactDaprNetworkAttachments(compose);
```

在 `ComposeText_FailsClosedForKafkaAuthenticationAndHostSafety` 的 forbidden token 集合加入：

```csharp
"ipv4_address:",
"ipv6_address:",
"link_local_ips:",
"DAPR_HOST_IP"
```

这样任何 sidecar 或单网络服务引入静态地址，以及任何 Dapr host-IP 环境覆盖，都会使合同测试失败。

新增变异测试：

```csharp
[Fact]
public void ComposeNetworkValidator_RejectsInterfaceAndGatewayMutations()
{
    var compose = ReadRequired(ComposePath);
    var wrongInterface = ReplaceFirst(compose, "interface_name: eth0", "interface_name: eth9");
    Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(wrongInterface));

    var weakRuntimeGateway = ReplaceFirst(compose, "gw_priority: 1", "gw_priority: 0");
    Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(weakRuntimeGateway));

    var strongPrivateGateway = ReplaceFirst(compose, "gw_priority: 0", "gw_priority: 2");
    Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(strongPrivateGateway));
}
```

加入规范化 JSON helper：

```csharp
private static void AssertJsonNetworkAttachment(
    JsonElement services,
    string service,
    string network,
    string interfaceName,
    int gatewayPriority)
{
    var attachment = services.GetProperty(service).GetProperty("networks").GetProperty(network);
    Assert.Equal(interfaceName, attachment.GetProperty("interface_name").GetString());
    Assert.Equal(gatewayPriority, attachment.GetProperty("gw_priority").GetInt32());
}
```

在 `DockerComposeConfig_WhenAvailable_PreservesTheStaticSecurityContract` 的 `AssertJsonNetworks` 后加入全部六个断言：

```csharp
AssertJsonNetworkAttachment(services, "publisher-dapr", "runtime", "eth0", 1);
AssertJsonNetworkAttachment(services, "publisher-dapr", "publisher-app", "eth1", 0);
AssertJsonNetworkAttachment(services, "receiver-dapr", "runtime", "eth0", 1);
AssertJsonNetworkAttachment(services, "receiver-dapr", "receiver-app", "eth1", 0);
AssertJsonNetworkAttachment(services, "unauthorized-dapr", "runtime", "eth0", 1);
AssertJsonNetworkAttachment(services, "unauthorized-dapr", "unauthorized-app", "eth1", 0);
```

现有断言必须继续证明只有 sidecar 双网卡、Kafka 仅在 `runtime`、应用/direct probe 仅在私网、无 `network_mode`、无额外 host port。

- [ ] **Step 2: Run the focused contract test and confirm RED**

```powershell
& $dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09ComposeContractTests" --no-restore
```

Expected: FAIL at `AssertExactDaprNetworkAttachments` because the current sidecar network entries are empty maps. The failure is static; it does not create containers.

- [ ] **Step 3: Add exact interface and gateway fields to all three sidecars**

把 `publisher-dapr` 的网络块改为：

```yaml
    networks:
      runtime:
        interface_name: eth0
        gw_priority: 1
      publisher-app:
        interface_name: eth1
        gw_priority: 0
```

把 `receiver-dapr` 的网络块改为：

```yaml
    networks:
      runtime:
        interface_name: eth0
        gw_priority: 1
      receiver-app:
        interface_name: eth1
        gw_priority: 0
```

把 `unauthorized-dapr` 的网络块改为：

```yaml
    networks:
      runtime:
        interface_name: eth0
        gw_priority: 1
      unauthorized-app:
        interface_name: eth1
        gw_priority: 0
```

不要修改其他服务的网络、`ports`、mount、profile、user、image 或 dependency。

- [ ] **Step 4: Run text and rendered-Compose gates and confirm GREEN**

```powershell
& $dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09ComposeContractTests" --no-restore
docker compose --file deploy/p09/compose/compose.yaml --profile negative --profile provision config --quiet
```

Expected: focused xUnit filter passes with zero skips; Compose config exits 0 under Compose `>=2.36.0`. No container is started.

- [ ] **Step 5: Review and commit only deterministic networking**

```powershell
git diff --check
git diff -- deploy/p09/compose/compose.yaml tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs
git add -- deploy/p09/compose/compose.yaml tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs
git commit -m "fix: pin P09 Dapr runtime interfaces"
```

Expected: diff contains exactly six `interface_name` values and six `gw_priority` values; no application joins `runtime`.

### Task 3: Separate Kafka readiness and provision client deadlines

**Files:**
- Modify: `tests/p09/compose-rehearsal.Tests.ps1:98-130`
- Modify: `tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs:521-560,760-785`
- Modify: `eng/p09/P09Rehearsal.psm1:602-650,684-705`
- Modify: `deploy/p09/compose/compose.yaml:70-87`

- [ ] **Step 1: Write RED tests for exact generated timeouts and readiness paths**

把现有两个固定检查 `request.timeout.ms=5000` 与 `default.api.timeout.ms=5000` 的 source-text 断言替换为：

```powershell
$readinessProperties = & $module {
    New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password 'redacted-test-value' -TimeoutMilliseconds 5000
}
$provisionerProperties = & $module {
    New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password 'redacted-test-value' -TimeoutMilliseconds 30000
}
Assert-True ($readinessProperties.Contains("request.timeout.ms=5000`ndefault.api.timeout.ms=5000`n")) 'Readiness client timeout drifted.'
Assert-True ($provisionerProperties.Contains("request.timeout.ms=30000`ndefault.api.timeout.ms=30000`n")) 'Provision client timeout drifted.'
Assert-True ($moduleText -match "(?m)kafka/clients','readiness\.properties','1000:1000'.*TimeoutMilliseconds 5000") 'Runtime population omits readiness.properties.'
Assert-True ($moduleText -match "(?m)kafka/clients','provisioner\.properties','1000:1000'.*TimeoutMilliseconds 30000") 'Runtime population does not isolate provisioner timeouts.'
```

把 fake-Docker readiness 命令的期望配置路径改为：

```powershell
'--bootstrap-server','kafka:9092','--command-config','/etc/kafka/clients/readiness.properties','--list'
```

把 `AssertExactComposeRuntimeFields` 和 `AssertJsonRuntimeFields` 中 Kafka healthcheck 的期望路径都改为：

```text
/etc/kafka/clients/readiness.properties
```

其他 Topic/ACL provision 测试仍必须精确要求 `/etc/kafka/clients/provisioner.properties`。

- [ ] **Step 2: Run focused tests and confirm RED**

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
& $dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09ComposeContractTests" --no-restore
```

Expected: PowerShell test fails because `New-Cp6P09ClientProperties` has no `TimeoutMilliseconds` parameter/readiness file, and xUnit fails because Compose healthcheck still uses `provisioner.properties`.

- [ ] **Step 3: Generate two bounded client configurations and route readiness to the short one**

把 generator 改为：

```powershell
function New-Cp6P09ClientProperties {
    param(
        [Parameter(Mandatory)][string]$Username,
        [Parameter(Mandatory)][string]$Password,
        [Parameter(Mandatory)][ValidateSet(5000, 30000)][int]$TimeoutMilliseconds
    )
    return @"
security.protocol=SASL_PLAINTEXT
sasl.mechanism=PLAIN
sasl.jaas.config=org.apache.kafka.common.security.plain.PlainLoginModule required username="$Username" password="$Password";
request.timeout.ms=$TimeoutMilliseconds
default.api.timeout.ms=$TimeoutMilliseconds
"@.Replace("`r`n", "`n")
}
```

把 `Initialize-Cp6P09RuntimeFiles` 中四个现有 client 条目和新的 readiness 条目写成：

```powershell
@('kafka/clients','readiness.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password $Credentials.Provisioner -TimeoutMilliseconds 5000)),
@('kafka/clients','provisioner.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-provisioner' -Password $Credentials.Provisioner -TimeoutMilliseconds 30000)),
@('kafka/clients','publisher.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-probe-publisher' -Password $Credentials.Publisher -TimeoutMilliseconds 30000)),
@('kafka/clients','receiver.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-probe-receiver' -Password $Credentials.Receiver -TimeoutMilliseconds 30000)),
@('kafka/clients','unauthorized.properties','1000:1000',(New-Cp6P09ClientProperties -Username 'cp6-p09-unauthorized-probe' -Password $Credentials.Unauthorized -TimeoutMilliseconds 30000)),
```

把 `Wait-Cp6P09KafkaDataPlane` 的 `--command-config` 改为：

```powershell
'--command-config','/etc/kafka/clients/readiness.properties'
```

把 `compose.yaml` Kafka healthcheck 的配置路径改为：

```yaml
        - /etc/kafka/clients/readiness.properties
```

不得改变 ACL/Topic provision 使用的 `provisioner.properties`、外层 120 秒 deadline、文件 UID/GID、临时根或只读 mount。

- [ ] **Step 4: Run script, contract, and cleanup gates and confirm GREEN**

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
& $dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09ComposeContractTests" --no-restore
```

Expected: all three commands pass; only readiness paths contain 5-second client timeouts, while provision/client files contain 30-second timeouts.

- [ ] **Step 5: Review and commit only Kafka deadline separation**

```powershell
git diff --check
git diff -- deploy/p09/compose/compose.yaml eng/p09/P09Rehearsal.psm1 tests/p09/compose-rehearsal.Tests.ps1 tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs
git add -- deploy/p09/compose/compose.yaml eng/p09/P09Rehearsal.psm1 tests/p09/compose-rehearsal.Tests.ps1 tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs
git commit -m "fix: isolate P09 Kafka readiness deadlines"
```

Expected: one focused commit; no Dapr component, ACL tuple or evidence schema change.

### Task 4: Prove the exact-SHA Compose matrix and zero residue

**Files:**
- Verify only: `eng/run-p09-compose-rehearsal.ps1`
- Verify only: `compose-rehearsal-result.v1.json` under the ignored artifact directory returned in `$summary.ArtifactsDirectory`

- [ ] **Step 1: Run all non-runtime regression gates**

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
& $dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --no-restore
git diff --check 64a19b5951d7eb55bb0db298903a289a225e6378..HEAD
```

Expected: PowerShell scripts print `PASS`; the Deployment suite is all green with no skipped P09 contract test; diff check is clean.

- [ ] **Step 2: Review the complete calibration diff before runtime**

```powershell
git diff --stat 64a19b5951d7eb55bb0db298903a289a225e6378..HEAD
git diff 64a19b5951d7eb55bb0db298903a289a225e6378..HEAD -- deploy/p09/compose/compose.yaml eng/p09/P09Rehearsal.psm1 tests/p09/compose-rehearsal.Tests.ps1 tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs docs/superpowers/plans/2026-08-31-p09-compose-dapr-runtime-nic.md
git status --short
```

Expected: only this plan plus the four mapped implementation/test files changed after the design commit; no Secret, machine path, debug command, scope drift or unstaged file; status is clean.

- [ ] **Step 3: Run the real rehearsal against the exact clean HEAD**

```powershell
$exactSha = git rev-parse HEAD
$runOutput = @(& pwsh -NoProfile -File eng/run-p09-compose-rehearsal.ps1 -ExpectedGitSha $exactSha 2>&1)
$runExit = $LASTEXITCODE
$summary = $runOutput[-1] | ConvertFrom-Json
$summary | ConvertTo-Json -Depth 8
```

Expected while Task 7 assets are still absent:

```text
exit code: 1
Status: Failed
Reason: kubernetes-assets-pending
ZeroResidue: true
```

This is the approved Task 6 success boundary: the Compose matrix completed, but the runner correctly refuses complete P09 Passed evidence before Kubernetes checks exist. If `OriginalFailureId` is `invoke-positive` or any matrix/provision/cleanup check fails, stop this plan, retain only bounded redacted artifacts, report the exact closed failure category, and do not weaken topology, ACL or cleanup gates.

到达该边界同时证明现有 runner 已顺序通过 `invoke-positive`（含 trace parent/child 拓扑）、`pubsub-positive`、`direct-kafka-denied`、未授权 principal produce/consume、`appid-scope-denied` 和 Docker 前拒绝 foreign topic；这些检查中的任何一个失败都不能用 `kubernetes-assets-pending` 掩盖。

- [ ] **Step 4: Validate the Compose result artifact and exact project cleanup**

```powershell
$artifactDirectory = Join-Path (Get-Location) $summary.ArtifactsDirectory
$composeResultPath = Join-Path $artifactDirectory 'compose-rehearsal-result.v1.json'
$composeResult = Get-Content -Raw -LiteralPath $composeResultPath | ConvertFrom-Json
if ($composeResult.composeChecks -cne 'Passed') { throw 'composeChecks is not Passed.' }
if ($composeResult.zeroResidue -ne $true) { throw 'zeroResidue is not true.' }
if ($composeResult.kubernetesEvidence -cne 'Pending') { throw 'Kubernetes dependency is not explicitly Pending.' }
if ($composeResult.platformGitSha -cne $exactSha) { throw 'Artifact SHA does not match exact HEAD.' }

$projectName = 'cp6-p09-' + (($summary.RunId -split '-')[-1])
$counts = [ordered]@{
    containers = @(docker ps -aq --filter "label=com.docker.compose.project=$projectName").Count
    networks = @(docker network ls -q --filter "label=com.docker.compose.project=$projectName").Count
    volumes = @(docker volume ls -q --filter "label=com.docker.compose.project=$projectName").Count
    images = @(docker image ls -q --filter "label=com.docker.compose.project=$projectName").Count
}
$counts | ConvertTo-Json -Compress
if (@($counts.Values | Where-Object { $_ -ne 0 }).Count -ne 0) { throw 'Exact Compose project residue remains.' }
$runtimeRoot = Join-Path ([IO.Path]::GetTempPath()) ("cp6-p09-$($summary.RunId)")
if (Test-Path -LiteralPath $runtimeRoot) { throw 'P09 runtime credential root remains.' }
```

Expected: artifact values match exact HEAD; container/network/volume/image counts are all `0`; the computed runtime root does not exist.

- [ ] **Step 5: Hand back to the original P09 plan without claiming full completion**

```powershell
git status --short --branch
git log --oneline -5
```

Expected: clean branch containing three focused implementation commits after this plan. Record Task 6 Compose as verified, then resume Task 7 in `docs/superpowers/plans/2026-08-30-p09-s01-s03-platform-runtime.md`. Do not create full `rehearsal-evidence.v1.json`, update project state to complete, publish a package, modify CRM, open a PR or merge from this calibration plan alone.

## Completion criteria

This plan is complete only when:

1. Compose `<2.36.0`, malformed output and plugin failure return `NotRun / unsupported-compose-version` before runtime side effects;
2. all three Dapr sidecars use exact `runtime/eth0/gw=1` plus private `eth1/gw=0` attachments;
3. application and Kafka network memberships remain unchanged;
4. readiness uses its own 5-second client properties and normal provision uses 30 seconds under the existing 120-second outer bound;
5. the exact-SHA real Compose matrix passes invocation/trace, Pub/Sub and every approved negative check before reaching `kubernetes-assets-pending`;
6. the result has `composeChecks=Passed`, exact project zero residue and no retained runtime credential root;
7. all changed files are covered by focused tests and three auditable commits.
