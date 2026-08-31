# P09 S01-S03 Platform Non-production Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `CP6.Platform` 中完成 P09 的 Profile/Schema/独立 Deployment 包、真实 Compose 演练和 Kubernetes 离线静态门禁，使精确 `main` 达到 `Implemented / Rehearsal Candidate`，但不发布包、不修改 CRM、不声明 `Frozen / Consumable`。

**Architecture:** `CP6.Platform.Deployment` 是无外部运行时依赖的纯合同包；Profile 是 Compose 与 Kubernetes 的唯一输入。Compose runner 在唯一临时目录生成凭据和 Dapr runtime 资产，以真实 Dapr 1.18.2/Kafka 4.3.1 验证最小 ACL、AppId scope、网络隔离、证据封存和零残留。Kubernetes 使用 base + CI overlay，资源主体采用 JSON 以便用 `System.Text.Json` 做跨对象验证，再由固定 kubectl 容器完成离线 Kustomize render 与不可达 kubeconfig client dry-run。

**Tech Stack:** .NET 8/C# 12, xUnit, JsonSchema.Net 9.4.0, PowerShell 7, Docker Compose v2, Dapr 1.18.2, Apache Kafka 4.3.1, Kubernetes/Kustomize via `registry.k8s.io/kubectl:v1.34.1`, GitHub Actions.

---

## Scope check

本计划只覆盖一个可独立验收的 Platform 子系统：

- P09-S01：Profile、两个 Draft 2020-12 Schema、纯验证 API、独立包边界；
- P09-S02：真实 Compose provision、正负向运行、证据、清理；
- P09-S03：Kubernetes 离线 render/client dry-run/静态策略矩阵；
- exact-main 后的状态只能是 `Implemented / Rehearsal Candidate`。

以下工作有独立权限、身份或仓库边界，不进入本计划：

- P09-S04：从精确 Platform `main` 发布 `CP6.Platform.Deployment 0.9.0-alpha.1`；
- P09-S05：CRM 固定包版本黑盒消费；
- P09-S06：公共项目记忆同步与 Platform 最终冻结审计。

S04、S05、S06 必须在本计划 exact-main 证据成立后各自形成实施计划。不得在本分支修改 `publish-alpha.yml`、`CP6.CRM`、公共仓库或任何真实环境配置。

## Fixed contract values

实现不得自行改变下表；任何变更先回到设计评审。

| Contract | Exact value |
| --- | --- |
| Repository version | `0.9.0.0` |
| Deployment package | `0.9.0-alpha.1` |
| Profile ID | `cp6-platform-p09-ci-v1` |
| Environment | `NonProduction` |
| Dapr image | `daprio/daprd:1.18.2` |
| Kafka image | `apache/kafka:4.3.1` |
| kubectl image | `registry.k8s.io/kubectl:v1.34.1` |
| Topic | `cp6.platform.deployment-probe.v1` |
| Partitions | `3` |
| Consumer group | `cp6-p09-probe-receiver-v1` |
| Publisher AppId/principal | `cp6-p09-probe-publisher` |
| Receiver AppId/principal | `cp6-p09-probe-receiver` |
| Provisioner principal | `cp6-p09-provisioner` |
| Negative principal/AppId | `cp6-p09-unauthorized-probe` |
| Publish component | `cp6-p09-kafka-publish` |
| Subscribe component | `cp6-p09-kafka-subscribe` |
| Subscription | `cp6-p09-deployment-probe-subscription` |
| Kubernetes namespace | `cp6-p09-ci` |
| Probe event type | `com.gtx537.platform.contract-example.changed.v1` |

## Task 0: Land the approved S00 design and create a clean implementation worktree

**Files:**

- Existing design branch only: `docs/superpowers/specs/2026-08-30-p09-non-production-runtime-design.md`
- Existing design branch only: `docs/superpowers/plans/2026-08-30-p09-s01-s03-platform-runtime.md`
- New implementation worktree: `D:\CP6.Platform-worktrees\p09-nonprod-runtime-implementation`

- [ ] **Step 1: Verify the design branch contains documentation only**

```powershell
git status --short
git diff --check
git diff origin/main...HEAD --stat
git diff origin/main...HEAD -- src tests deploy eng .github
```

Expected: clean status after the plan commit; the final command has no output; the branch diff contains only the approved design and implementation-plan Markdown files.

- [ ] **Step 2: Push and open the S00 documentation PR**

```powershell
git push -u origin codex/p09-nonprod-runtime-design
$designPrUrl = gh pr create --base main --head codex/p09-nonprod-runtime-design --title "docs: design P09 non-production runtime" --body "Approves the P09 public non-production runtime semantics, solo-development gates, S01-S06 state machine, and the executable S01-S03 implementation plan. No runtime, package publication, CRM change, cloud resource, or deployment is included."
$designPrNumber = gh pr view $designPrUrl --json number --jq .number
gh pr checks $designPrNumber --watch
```

Expected: all documentation/validation checks required by the repository pass. Do not bypass a failing gate.

- [ ] **Step 3: Merge S00 and verify exact remote main**

```powershell
gh pr merge $designPrNumber --merge
git fetch origin
$designMergeSha = git rev-parse origin/main
gh pr view $designPrNumber --json state,mergeCommit --jq '{state:.state,sha:.mergeCommit.oid}'
```

Expected: PR state is `MERGED` and its merge commit equals `$designMergeSha` unless another authorized main commit landed afterward; in that case inspect intervening commits and use the latest confirmed `origin/main` baseline.

- [ ] **Step 4: Create the one-task implementation branch/worktree from confirmed main**

Run from the canonical repository `D:\CP6\CP6.Platform`:

```powershell
$implementationWorktree = 'D:\CP6.Platform-worktrees\p09-nonprod-runtime-implementation'
git fetch origin
git worktree add -b codex/p09-nonprod-runtime-implementation $implementationWorktree origin/main
git -C $implementationWorktree status --short --branch
git -C $implementationWorktree rev-parse HEAD
```

Expected: clean worktree, branch `codex/p09-nonprod-runtime-implementation`, HEAD equal to the confirmed `origin/main`. Do not reuse or clean the dirty root workspace.

## Task 1: Establish the independent package and test boundary

**Files:**

- Create: `src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj`
- Create: `tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj`
- Create: `tests/CP6.Platform.DeploymentTests/P09ProjectBoundaryTests.cs`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`
- Modify: `CP6.Platform.sln`

- [ ] **Step 1: Write the failing repository boundary tests**

Add `"CP6.Platform.Deployment" = []` to `ExpectedDependencies` and add these assertions to `RepositoryArchitectureTests`:

```csharp
[Fact]
public void Deployment_package_is_independent_and_existing_packages_do_not_reference_it()
{
    var projects = LoadProjects();
    var deployment = projects["CP6.Platform.Deployment"].Document;

    Assert.Empty(deployment.Descendants("ProjectReference"));
    Assert.Empty(deployment.Descendants("PackageReference"));
    Assert.Empty(deployment.Descendants("FrameworkReference"));

    foreach (var project in projects.Where(project => project.Key != "CP6.Platform.Deployment"))
    {
        Assert.DoesNotContain(
            project.Value.Document.Descendants("ProjectReference"),
            reference => string.Equals(
                Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value),
                "CP6.Platform.Deployment",
                StringComparison.Ordinal));
    }
}

[Fact]
public void Deployment_is_the_only_package_with_p09_assets()
{
    var projects = LoadProjects();
    var packedAssets = projects["CP6.Platform.Deployment"].Document
        .Descendants("None")
        .Where(item => string.Equals(item.Attribute("Pack")?.Value, "true", StringComparison.OrdinalIgnoreCase))
        .Select(item => (string?)item.Attribute("Pack"))
        .ToArray();
    Assert.Equal(["true", "true"], packedAssets);

    var otherIncludes = projects.Where(project => project.Key != "CP6.Platform.Deployment")
        .SelectMany(project => project.Value.Document.Descendants("None"))
        .Select(item => (string?)item.Attribute("Include"))
        .Where(value => value is not null)
        .ToArray();
    Assert.DoesNotContain(otherIncludes, value =>
        value!.Contains("contracts/p09", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("contracts\\p09", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("deploy/p09", StringComparison.OrdinalIgnoreCase) ||
        value.Contains("deploy\\p09", StringComparison.OrdinalIgnoreCase));
}
```

Also add a `CP6.Platform.Deployment` branch to the existing `P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded` packed-asset assertion:

```csharp
else if (packageId == "CP6.Platform.Deployment")
{
    Assert.Equal(
        [
            "contracts/p09/%(RecursiveDir)%(Filename)%(Extension)",
            "deploy/p09/%(RecursiveDir)%(Filename)%(Extension)"
        ],
        packedAssets);
}
```

Create `P09ProjectBoundaryTests.cs`:

```csharp
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09ProjectBoundaryTests
{
    [Fact]
    public void Assembly_has_the_expected_public_identity()
    {
        Assert.Equal("CP6.Platform.Deployment", typeof(Cp6P09RuntimeProfile).Assembly.GetName().Name);
    }

    [Fact]
    public void Package_code_does_not_reference_process_or_environment_apis()
    {
        var assemblyPath = typeof(Cp6P09RuntimeProfile).Assembly.Location;
        var bytes = File.ReadAllBytes(assemblyPath);
        var text = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("System.Diagnostics.Process", text, StringComparison.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Docker", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("kubectl", text, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the focused tests and confirm RED**

Run:

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --filter "FullyQualifiedName~RepositoryArchitectureTests"
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj
```

Expected: the first command fails because the Deployment project is absent; the second fails because the test project is absent.

- [ ] **Step 3: Create the production project**

Use this complete project file:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <AssemblyName>CP6.Platform.Deployment</AssemblyName>
    <RootNamespace>CP6.Platform.Deployment</RootNamespace>
    <Description>Versioned non-production runtime contracts for CP6 Platform.</Description>
    <PackageId>CP6.Platform.Deployment</PackageId>
    <IsPackable>true</IsPackable>
    <VersionPrefix>0.9.0</VersionPrefix>
    <VersionSuffix>alpha.1</VersionSuffix>
    <PackageReadmeFile>README.md</PackageReadmeFile>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
  </PropertyGroup>
  <ItemGroup>
    <None Include="..\..\contracts\p09\**\*" Pack="true" PackagePath="contracts\p09\%(RecursiveDir)%(Filename)%(Extension)" />
    <None Include="..\..\deploy\p09\**\*" Pack="true" PackagePath="deploy\p09\%(RecursiveDir)%(Filename)%(Extension)" />
  </ItemGroup>
</Project>
```

This project intentionally has no `ProjectReference` or `PackageReference`.

- [ ] **Step 4: Create the test project**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="JsonSchema.Net" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\CP6.Platform.Deployment\CP6.Platform.Deployment.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
</Project>
```

- [ ] **Step 5: Add both projects to the solution and wire the architecture test reference**

Run:

```powershell
dotnet sln CP6.Platform.sln add src/CP6.Platform.Deployment/CP6.Platform.Deployment.csproj
dotnet sln CP6.Platform.sln add tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj
```

- [ ] **Step 6: Add a compile-only profile shell and confirm GREEN**

Create `src/CP6.Platform.Deployment/Cp6P09RuntimeProfile.cs`:

```csharp
namespace CP6.Platform.Deployment;

public sealed partial class Cp6P09RuntimeProfile
{
    private Cp6P09RuntimeProfile() { }
}
```

Run the two focused test commands again. Expected: both pass.

- [ ] **Step 7: Commit the boundary**

```powershell
git add CP6.Platform.sln src/CP6.Platform.Deployment tests/CP6.Platform.DeploymentTests tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git commit -m "feat: establish P09 deployment package boundary"
```

## Task 2: Implement the strict canonical Profile API

**Files:**

- Replace: `src/CP6.Platform.Deployment/Cp6P09RuntimeProfile.cs`
- Create: `src/CP6.Platform.Deployment/Cp6P09ContractException.cs`
- Create: `src/CP6.Platform.Deployment/Cp6P09Json.cs`
- Create: `tests/CP6.Platform.DeploymentTests/P09RuntimeProfileTests.cs`

- [ ] **Step 1: Write the RED parsing and invariant matrix**

The test class must cover these exact cases with one named theory row per rejection:

```csharp
public static TheoryData<string, string> Rejections => new()
{
    { "duplicate-property", "{\"schemaVersion\":\"1\",\"schemaVersion\":\"1\"}" },
    { "unknown-property", MutateValid(root => root["extra"] = true) },
    { "production-environment", MutateValid(root => root["environmentClass"] = "Production") },
    { "crm-app-id", MutateValid(root => root["identities"]!["publisherAppId"] = "cp6.crm.publisher") },
    { "crm-topic", MutateValid(root => root["topic"]!["name"] = "cp6.crm.customer.changed.v1") },
    { "floating-dapr-image", MutateValid(root => root["runtime"]!["daprImage"] = "daprio/daprd:latest") },
    { "floating-kafka-image", MutateValid(root => root["runtime"]!["kafkaImage"] = "apache/kafka:4") },
    { "external-host", MutateValid(root => root["compose"]!["bootstrapServers"] = "broker.example.com:9092") },
    { "fixed-public-port", MutateValid(root => root["compose"]!["hostBinding"] = "0.0.0.0:3500") },
    { "wrong-partitions", MutateValid(root => root["topic"]!["partitions"] = 4) },
    { "write-on-receiver", MutateValid(root => ((JsonArray)root["acls"]!).Add(new JsonObject
        { ["principal"] = "cp6-p09-probe-receiver", ["resourceType"] = "Topic", ["resourceName"] = "cp6.platform.deployment-probe.v1", ["operation"] = "Write" })) },
};

[Theory]
[MemberData(nameof(Rejections))]
public void Parse_rejects_non_canonical_profiles(string id, string json)
{
    var exception = Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));
    Assert.Equal(id, exception.CheckId);
}

[Fact]
public void Parse_round_trips_the_canonical_profile()
{
    var profile = Cp6P09RuntimeProfile.Parse(ValidProfileJson);

    Assert.Equal("1", profile.SchemaVersion);
    Assert.Equal("NonProduction", profile.EnvironmentClass);
    Assert.Equal(Cp6P09RuntimeProfile.ExpectedProfileId, profile.ProfileId);
    Assert.Equal(Cp6P09RuntimeProfile.ExpectedTopic, profile.TopicName);
    Assert.Equal(3, profile.Partitions);
    Assert.Equal(Cp6P09Json.Canonicalize(ValidProfileJson), profile.ToCanonicalUtf8());
}
```

`MutateValid` parses `ValidProfileJson` into `JsonNode`, applies the mutation and serializes compact JSON. Keep fixtures inside the test class until Task 3 moves the valid value to a checked-in example.

- [ ] **Step 2: Run the profile tests and confirm RED**

```powershell
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09RuntimeProfileTests"
```

Expected: compile failures for the missing API.

- [ ] **Step 3: Add the stable exception type**

```csharp
namespace CP6.Platform.Deployment;

public sealed class Cp6P09ContractException : Exception
{
    public Cp6P09ContractException(string checkId, string message)
        : base(message) => CheckId = checkId;

    public string CheckId { get; }
}
```

- [ ] **Step 4: Add strict JSON parsing and canonicalization**

`Cp6P09Json` must:

1. parse with `AllowTrailingCommas=false`, `CommentHandling=Disallow`, maximum depth 64;
2. use `Utf8JsonReader` to reject a duplicate property before materializing `JsonDocument`;
3. recursively sort object properties ordinally, preserve array order, emit UTF-8 without indentation and append no BOM/newline;
4. expose `Canonicalize(string)` and `Sha256Hex(ReadOnlySpan<byte>)`;
5. throw `Cp6P09ContractException("duplicate-property", ...)` for duplicates and `invalid-json` for malformed JSON.

Use this duplicate-key core rather than a post-parse dictionary:

```csharp
private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8)
{
    var reader = new Utf8JsonReader(utf8, new JsonReaderOptions
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    });
    var scopes = new Stack<HashSet<string>>();

    while (reader.Read())
    {
        if (reader.TokenType == JsonTokenType.StartObject)
            scopes.Push(new HashSet<string>(StringComparer.Ordinal));
        else if (reader.TokenType == JsonTokenType.EndObject)
            scopes.Pop();
        else if (reader.TokenType == JsonTokenType.PropertyName &&
                 !scopes.Peek().Add(reader.GetString()!))
            throw new Cp6P09ContractException("duplicate-property", "Duplicate JSON property is forbidden.");
    }
}
```

- [ ] **Step 5: Implement the immutable Profile view and exact invariants**

`Cp6P09RuntimeProfile` must expose only values needed by consumers:

```csharp
public const string ExpectedProfileId = "cp6-platform-p09-ci-v1";
public const string ExpectedTopic = "cp6.platform.deployment-probe.v1";
public const string ExpectedConsumerGroup = "cp6-p09-probe-receiver-v1";
public string SchemaVersion { get; }
public string EnvironmentClass { get; }
public string ProfileId { get; }
public string TopicName { get; }
public string EventType { get; }
public int Partitions { get; }
public string PublisherAppId { get; }
public string ReceiverAppId { get; }
public string UnauthorizedAppId { get; }
public string PublishComponentName { get; }
public string SubscribeComponentName { get; }
public byte[] ToCanonicalUtf8();
public string Sha256 { get; }
public static Cp6P09RuntimeProfile Parse(string json);
public static Cp6P09RuntimeProfile Parse(ReadOnlySpan<byte> utf8);
```

Validation uses an allowlist, not loose prefix checks. Require the exact root property set:

```text
schemaVersion, environmentClass, profileId, runtime, identities, components,
topic, acls, compose, kubernetes, evidence
```

Require exact nested sets declared by the Schema in Task 3. Verify:

- all fixed values in this plan;
- two and only two components, with publish scoped only to publisher and subscribe scoped only to receiver;
- exact ACL tuples: publisher Topic Write/Describe; receiver Topic Read/Describe and Group Read; provisioner Topic Create/Alter/Describe plus Cluster Describe; no tuple for unauthorized;
- Compose has separate `app`/`runtime` networks, Kafka host port disabled, loopback random test entry, host networking/privileged/socket/host paths all false;
- Kubernetes namespace is `cp6-p09-ci`, default deny/DNS/minimal ingress/Kafka egress are required, forbidden kinds list is exact;
- every string is non-empty NFC, contains no CR/NUL, and no JSON string matches secret-value fields `password`, `token`, `connectionString`, `secretValue` case-insensitively.

Each invariant must throw the stable test ID from the theory row; missing/unknown/type errors use `missing-property`, `unknown-property`, and `wrong-type`.

- [ ] **Step 6: Run the profile tests and full Deployment tests**

```powershell
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09RuntimeProfileTests"
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj
```

Expected: pass with zero skipped tests.

- [ ] **Step 7: Commit the Profile API**

```powershell
git add src/CP6.Platform.Deployment tests/CP6.Platform.DeploymentTests
git commit -m "feat: add strict P09 runtime profile API"
```

## Task 3: Freeze Profile and evidence Schemas with mutation tests

**Files:**

- Create: `contracts/p09/non-production-runtime-profile.v1.schema.json`
- Create: `contracts/p09/rehearsal-evidence.v1.schema.json`
- Create: `contracts/p09/examples/non-production-runtime-profile.valid.json`
- Create: `contracts/p09/examples/non-production-runtime-profile.crm-topic.invalid.json`
- Create: `contracts/p09/examples/non-production-runtime-profile.plaintext-secret.invalid.json`
- Create: `contracts/p09/examples/non-production-runtime-profile.production.invalid.json`
- Create: `contracts/p09/examples/rehearsal-evidence.valid.json`
- Create: `contracts/p09/examples/rehearsal-evidence.secret.invalid.json`
- Create: `src/CP6.Platform.Deployment/Cp6P09RehearsalEvidence.cs`
- Create: `tests/CP6.Platform.DeploymentTests/P09SchemaTests.cs`
- Create: `tests/CP6.Platform.DeploymentTests/P09EvidenceTests.cs`

- [ ] **Step 1: Write schema discovery and parity tests**

Tests must load files by walking from `AppContext.BaseDirectory` to the repository marker `CP6.Platform.sln`; never hard-code `D:\` or a user directory.

```csharp
[Fact]
public void Valid_profile_passes_schema_and_runtime_validator()
{
    var result = ProfileSchema.Evaluate(JsonNode.Parse(Read("non-production-runtime-profile.valid.json")),
        new EvaluationOptions { OutputFormat = OutputFormat.List });
    Assert.True(result.IsValid, result.ToJsonString());

    var profile = Cp6P09RuntimeProfile.Parse(Read("non-production-runtime-profile.valid.json"));
    Assert.Equal(Cp6P09RuntimeProfile.ExpectedProfileId, profile.ProfileId);
}

[Theory]
[InlineData("non-production-runtime-profile.crm-topic.invalid.json")]
[InlineData("non-production-runtime-profile.plaintext-secret.invalid.json")]
[InlineData("non-production-runtime-profile.production.invalid.json")]
public void Invalid_profiles_fail_schema_and_runtime_validator(string file)
{
    Assert.False(ProfileSchema.Evaluate(JsonNode.Parse(Read(file))).IsValid);
    Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(Read(file)));
}

[Fact]
public void Schemas_are_draft_2020_12_and_close_every_object()
{
    foreach (var file in new[] { ProfileSchemaPath, EvidenceSchemaPath })
    {
        var root = JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root["$schema"]!.GetValue<string>());
        AssertAllObjectsCloseUnknownProperties(root);
    }
}
```

- [ ] **Step 2: Confirm RED**

Run the schema/evidence test filters. Expected: missing contract files and evidence type failures.

- [ ] **Step 3: Write the canonical Profile example**

The canonical example must contain the exact values below and no additional identities, topics or endpoints:

```json
{
  "schemaVersion": "1",
  "environmentClass": "NonProduction",
  "profileId": "cp6-platform-p09-ci-v1",
  "runtime": {
    "daprImage": "daprio/daprd:1.18.2",
    "kafkaImage": "apache/kafka:4.3.1",
    "kubectlImage": "registry.k8s.io/kubectl:v1.34.1"
  },
  "identities": {
    "publisherAppId": "cp6-p09-probe-publisher",
    "receiverAppId": "cp6-p09-probe-receiver",
    "provisionerPrincipal": "cp6-p09-provisioner",
    "unauthorizedAppId": "cp6-p09-unauthorized-probe",
    "consumerGroup": "cp6-p09-probe-receiver-v1"
  },
  "components": [
    { "name": "cp6-p09-kafka-publish", "direction": "Publish", "scope": ["cp6-p09-probe-publisher"], "usernameSecretRef": "publisher-username", "passwordSecretRef": "publisher-password" },
    { "name": "cp6-p09-kafka-subscribe", "direction": "Subscribe", "scope": ["cp6-p09-probe-receiver"], "usernameSecretRef": "receiver-username", "passwordSecretRef": "receiver-password" }
  ],
  "topic": {
    "name": "cp6.platform.deployment-probe.v1",
    "eventType": "com.gtx537.platform.contract-example.changed.v1",
    "partitions": 3,
    "retentionMs": 3600000,
    "maxMessageBytes": 1048576
  },
  "acls": [
    { "principal": "cp6-p09-probe-publisher", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Write" },
    { "principal": "cp6-p09-probe-publisher", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Describe" },
    { "principal": "cp6-p09-probe-receiver", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Read" },
    { "principal": "cp6-p09-probe-receiver", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Describe" },
    { "principal": "cp6-p09-probe-receiver", "resourceType": "Group", "resourceName": "cp6-p09-probe-receiver-v1", "operation": "Read" },
    { "principal": "cp6-p09-provisioner", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Create" },
    { "principal": "cp6-p09-provisioner", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Alter" },
    { "principal": "cp6-p09-provisioner", "resourceType": "Topic", "resourceName": "cp6.platform.deployment-probe.v1", "operation": "Describe" },
    { "principal": "cp6-p09-provisioner", "resourceType": "Cluster", "resourceName": "kafka-cluster", "operation": "Describe" }
  ],
  "compose": {
    "appNetwork": "app",
    "runtimeNetwork": "runtime",
    "bootstrapServers": "kafka:9092",
    "kafkaHostPort": false,
    "hostBinding": "127.0.0.1:0",
    "hostNetwork": false,
    "privileged": false,
    "dockerSocket": false,
    "hostPath": false
  },
  "kubernetes": {
    "namespace": "cp6-p09-ci",
    "nonDeployableLabel": "cp6.io/nondeployable=true",
    "defaultDeny": true,
    "dnsEgress": true,
    "minimalProbeIngress": true,
    "minimalKafkaEgress": true,
    "forbiddenKinds": ["Secret", "ClusterRole", "ClusterRoleBinding", "Ingress", "PersistentVolume"]
  },
  "evidence": {
    "schemaId": "https://cp6.example/contracts/p09/rehearsal-evidence.v1.schema.json",
    "requiredChecks": ["profile-valid", "provision-first", "provision-idempotent", "invoke-positive", "pubsub-positive", "direct-kafka-denied", "principal-denied", "appid-scope-denied", "foreign-topic-denied", "kubernetes-render", "kubernetes-policy", "zero-residue"]
  }
}
```

- [ ] **Step 4: Write both Draft 2020-12 Schemas**

Use `$id` values under `https://cp6.example/contracts/p09/`, `type`, `required`, `additionalProperties:false`, exact `const`/`enum` constraints, numeric bounds, and array cardinality for every object above. The Profile Schema must reject the three invalid examples without relying on the C# validator.

The evidence Schema root is exactly:

```text
schemaVersion, profileId, profileSha256, platformGitSha, repositoryVersion,
packageVersion, composeManifestSha256, kubernetesManifestSha256, runtime,
topic, acls, checks, trace, startedUtc, completedUtc, teardown, overall
```

Use `^[0-9a-f]{64}$` for SHA-256, `^[0-9a-f]{40}$` for Git SHA, RFC 3339 UTC strings ending in `Z`, `Passed|Failed` for outcomes, and `additionalProperties:false` everywhere. `checks` is a non-empty array of `{id,result,summary}` with `result=Passed|Failed`; `teardown` requires command exit code, container/network/volume/image counts and `temporaryDirectoryRemoved`.

- [ ] **Step 5: Implement the evidence API**

`Cp6P09RehearsalEvidence.Parse` must use `Cp6P09Json`, reject unknown/duplicate properties, verify the exact required check ID set from the Profile, reject free-form secrets/machine paths, and enforce:

```csharp
if (Overall == "Passed" && (Checks.Any(check => check.Result != "Passed") ||
    !Teardown.TemporaryDirectoryRemoved || Teardown.ResourceCount != 0))
    throw new Cp6P09ContractException("false-pass", "Passed evidence requires all checks and zero residue.");
```

Expose canonical UTF-8 and SHA-256 exactly as Profile does. A failed evidence fixture may retain stable summaries but never secret values.

- [ ] **Step 6: Add mutation coverage**

Generate mutations in memory for: missing check, duplicate check, fake Passed, uppercase hash, local Windows path, `/home/runner` path, `password=`, bearer token, unknown property, non-UTC time, wrong profile hash, and non-canonical JSON. Every mutation must fail both Schema and runtime validator where structurally applicable.

- [ ] **Step 7: Run and commit**

```powershell
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj
git add contracts/p09 src/CP6.Platform.Deployment tests/CP6.Platform.DeploymentTests
git commit -m "feat: freeze P09 profile and evidence contracts"
```

Expected: all tests pass with zero skipped.

## Task 4: Build the isolated P09 probe fixture

**Files:**

- Create: `tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj`
- Create: `tests/CP6.Platform.P09Fixture/Program.cs`
- Create: `tests/CP6.Platform.P09Fixture/Dockerfile`
- Create: `tests/CP6.Platform.DeploymentTests/P09FixtureBoundaryTests.cs`
- Modify: `CP6.Platform.sln`

- [ ] **Step 1: Write RED boundary tests**

Assert the fixture references only `CP6.Platform.Contracts`, `CP6.Platform.Messaging`, and `CP6.Platform.Deployment`; source must not contain `crm`, database packages, connection strings, business topics, or a Kafka client package. Assert the Dockerfile has a fixed .NET 8 base image tag and runs as non-root.

- [ ] **Step 2: Create the project**

Use `Microsoft.NET.Sdk.Web`, `IsPackable=false`, and project references to the three packages above. Reuse the centrally managed Dapr ASP.NET Core package; do not add a Kafka client.

- [ ] **Step 3: Implement four fixed roles**

The executable accepts one role argument: `publisher`, `receiver`, `probe`, `unauthorized`. Unknown roles exit 64.

Required endpoints:

| Role | Endpoint | Behavior |
| --- | --- | --- |
| publisher | `GET /healthz` | `200` only after Profile loads |
| publisher | `POST /invoke-positive` | Dapr invoke receiver `/invoked`, returns receiver correlation |
| publisher | `POST /publish-positive` | publish one structured CloudEvent to Profile Topic/component with fixed region `TEST` and caller-provided event ID/partition key |
| receiver | `GET /dapr/subscribe` | only Profile Topic, subscribe component, route `/events/deployment-probe` |
| receiver | `POST /events/deployment-probe` | validate CloudEvent envelope/schema/region/topic/key/trace and store one bounded result in memory |
| receiver | `POST /invoked` | return trace/correlation data |
| receiver | `GET /received/{eventId}` | `404` until received, then stable JSON |
| probe | `GET /direct-kafka` | resolve/connect `kafka:9092`; success is treated as a test failure |
| unauthorized | `POST /publish` | call its local Dapr sidecar with publish component; component absence/denial is expected |

Use `DaprClient` only. Publish the existing P04 canonical structured CloudEvent `com.gtx537.platform.contract-example.changed.v1`; only the transport Topic is the P09-specific `cp6.platform.deployment-probe.v1` from the validated Profile. Do not add a new event contract or change production Messaging behavior to accept a new contract identity. Receiver uses `Cp6CloudEventValidator` for the existing bundle entry and P09 Profile checks for Topic/partition key.

- [ ] **Step 4: Add a deterministic Dockerfile**

Multi-stage build, fixed `mcr.microsoft.com/dotnet/sdk:8.0` and `aspnet:8.0` tags, `dotnet publish --no-restore`, non-root `USER app`, no package manager or shell-installed utilities. The build context is repository root.

- [ ] **Step 5: Run fixture boundary and build tests**

```powershell
dotnet sln CP6.Platform.sln add tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09FixtureBoundaryTests"
dotnet build tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj -c Release
```

- [ ] **Step 6: Commit**

```powershell
git add CP6.Platform.sln tests/CP6.Platform.P09Fixture tests/CP6.Platform.DeploymentTests
git commit -m "test: add isolated P09 runtime probe"
```

## Task 5: Define the Compose topology and generated-secret templates

**Files:**

- Create: `deploy/p09/compose/compose.yaml`
- Create: `deploy/p09/compose/templates/secret-store.yaml`
- Create: `deploy/p09/compose/templates/kafka-publish.yaml`
- Create: `deploy/p09/compose/templates/kafka-subscribe.yaml`
- Create: `deploy/p09/compose/templates/subscription.yaml`
- Create: `deploy/p09/compose/templates/kafka-server.properties`
- Create: `deploy/p09/compose/templates/kafka-jaas.conf`
- Create: `tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs`

- [ ] **Step 1: Write static RED tests before the Compose file**

Tests must parse Compose as text plus JSON-converted output from `docker compose config --format json` when Docker is available. Assert:

- exact image tags and no `latest`;
- Kafka has only `runtime`; publisher/receiver app containers have only their app network; each sidecar has its app network plus `runtime`;
- Kafka has no `ports`; publisher entry uses `${CP6_P09_HOST_PORT:-0}:8080` bound to `127.0.0.1`;
- no host network, privileged, Docker socket, host path, fixed external container name or external network;
- components use `authType: password`, `saslMechanism: PLAIN`, `secretKeyRef`, and exact scopes;
- no secret value, password literal or user-specific absolute path is present.

- [ ] **Step 2: Confirm RED**

Run the focused test. Expected: missing Compose assets.

- [ ] **Step 3: Create the topology**

Services are exactly:

```text
kafka, publisher, publisher-dapr, receiver, receiver-dapr,
direct-probe, unauthorized-dapr, kafka-admin
```

`kafka-admin` and negative helpers use Compose profiles so they do not remain running. All generated mounts are under `${CP6_P09_RUNTIME_ROOT}` and read-only. Named volumes are limited to one Kafka data volume; networks are `publisher-app`, `receiver-app`, `unauthorized-app`, and `runtime`.

Kafka uses KRaft combined mode, controller listener isolated from the client listener, `StandardAuthorizer`, `allow.everyone.if.no.acl.found=false`, and only the provisioner/admin identity in `super.users`. Client listener is SASL_PLAINTEXT because transport is internal to the isolated rehearsal network; no host port is exposed and the evidence must call this out as non-production. Never use `authType=none`.

- [ ] **Step 4: Create secret-free templates**

Templates contain tokens only for generated file locations and secret names. The local file secret store component is `secretstores.local.file`; Kafka components use exact P09 component names and `secretKeyRef`. The subscription references only the subscribe component and receiver AppId. A repository scan for actual generated usernames/passwords must find none.

- [ ] **Step 5: Verify and commit**

```powershell
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09ComposeContractTests"
git add deploy/p09/compose tests/CP6.Platform.DeploymentTests/P09ComposeContractTests.cs
git commit -m "feat: define isolated P09 compose topology"
```

## Task 6: Implement real Compose rehearsal, evidence, and zero-residue cleanup

**Files:**

- Create: `eng/p09/P09Rehearsal.psm1`
- Create: `eng/run-p09-compose-rehearsal.ps1`
- Create: `tests/p09/compose-rehearsal.Tests.ps1`
- Create: `tests/p09/cleanup-failure.Tests.ps1`
- Create: `tests/p09/fake-docker/docker.ps1`
- Modify: `.gitignore`

- [ ] **Step 1: Write RED script tests with a fake Docker executable**

The fake records argv as JSON lines and returns controlled exit codes. Cover:

1. invalid Profile exits before any Docker call;
2. first runtime error still invokes exact project `down --volumes --remove-orphans --rmi local`;
3. cleanup failure makes overall Failed;
4. runner never invokes prune, removes, or lists resources without the exact project label;
5. secret root is a unique child of `[IO.Path]::GetTempPath()` whose resolved path begins with the resolved temp root plus a directory separator;
6. artifacts remain inside `artifacts/p09-rehearsal/$runId`;
7. NotRun is returned when Docker is absent and no Passed evidence is written.

- [ ] **Step 2: Confirm RED**

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
```

Expected: fail because the module/runner is absent.

- [ ] **Step 3: Implement fail-before-side-effect preflight**

The runner parameters are exactly:

```powershell
param(
  [string]$ProfilePath = "contracts/p09/examples/non-production-runtime-profile.valid.json",
  [string]$ArtifactsRoot = "artifacts/p09-rehearsal",
  [string]$ExpectedGitSha,
  [switch]$KeepFailedArtifacts
)
```

Before Docker:

- resolve repository root from the script path;
- validate all supplied paths remain within repository artifacts or OS temp boundaries;
- run Deployment contract tests and parse Profile through a small `dotnet run` validator entry in the test project;
- run Compose/Kubernetes static contract filters;
- reject a dirty mutation to canonical assets when `ExpectedGitSha` is supplied;
- check Docker/Compose versions only after all content validation passes.

- [ ] **Step 4: Generate bounded ephemeral credentials**

Use `RandomNumberGenerator.GetBytes(32)` and Base64URL without padding. Generate independent provisioner, publisher, receiver and unauthorized passwords. Write UTF-8 without BOM to files with restrictive Windows ACL or Unix mode 0600. Generate Kafka JAAS, client property files and Dapr components from templates. Never pass password values on command lines or write them to artifacts/logs.

- [ ] **Step 5: Provision deterministically**

Use only `docker compose --project-name $project --file $composeFile` calls, where `$composeFile` is the resolved canonical `deploy/p09/compose/compose.yaml`. Sequence:

1. start Kafka;
2. wait bounded 120 seconds for broker health;
3. create Topic with exact partitions/config;
4. describe Topic/config and compare exact values;
5. add exact ACL tuples;
6. list/normalize ACLs and compare exact set;
7. replay create/ACL commands and prove the normalized state is unchanged;
8. abort on any drift without adding broader ACLs.

- [ ] **Step 6: Run the positive and negative matrix**

Start receiver/sidecar then publisher/sidecar. Use the loopback random host port returned by `docker compose port publisher 8080`. Run service invocation and Pub/Sub with unique event ID and partition key; poll receiver for at most 60 seconds.

Negative checks are exact:

- `direct-kafka-denied`: direct-probe cannot resolve/connect broker;
- `principal-denied`: unauthorized Kafka properties cannot produce or consume;
- `appid-scope-denied`: unauthorized Dapr AppId cannot see/use publish component;
- `foreign-topic-denied`: runner rejects `cp6.platform.other.v1` before a Docker command and broker Topic list remains unchanged.

Capture only bounded, redacted summaries. Verify trace parent/child topology and the event identity/topic/key.

- [ ] **Step 7: Seal evidence before teardown, then finalize after teardown**

Write normalized evidence to a temporary artifact file, calculate SHA-256, teardown in `finally`, add teardown results, revalidate, and atomically rename to:

```text
artifacts/p09-rehearsal/$runId/rehearsal-evidence.v1.json
artifacts/p09-rehearsal/$runId/rehearsal-evidence.v1.sha256
```

Also retain canonical, secret-scanned logs and manifest/digest summaries. Do not retain the generated runtime root.

- [ ] **Step 8: Implement exact cleanup**

Stop all secret users first, call exact Compose down, then query resources using label `com.docker.compose.project=$project`. Counts for containers, networks, volumes and locally built fixture images must all be zero. Delete the temp root only after the label check. Any cleanup error sets Failed and preserves the original error separately.

- [ ] **Step 9: Run script tests, then the real rehearsal**

```powershell
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
pwsh -NoProfile -File eng/run-p09-compose-rehearsal.ps1 -ExpectedGitSha (git rev-parse HEAD)
```

Expected: script tests pass; real run either produces Passed evidence with zero residue or an explicit NotRun environment error. A NotRun local result is acceptable for development but the GitHub Ubuntu job in Task 9 must later produce Passed for the same commit.

- [ ] **Step 10: Commit**

```powershell
git add .gitignore eng/p09 eng/run-p09-compose-rehearsal.ps1 tests/p09
git commit -m "feat: add real P09 compose rehearsal"
```

## Task 7: Add Kubernetes base/CI overlay and offline policy validation

**Files:**

- Create: `deploy/p09/kubernetes/base/kustomization.yaml`
- Create: `deploy/p09/kubernetes/base/*.json`
- Create: `deploy/p09/kubernetes/overlays/ci/kustomization.yaml`
- Create: `deploy/p09/kubernetes/overlays/ci/*.json`
- Create: `eng/test-p09-kubernetes.ps1`
- Create: `src/CP6.Platform.Deployment/Cp6P09KubernetesValidator.cs`
- Create: `tests/CP6.Platform.DeploymentTests/P09KubernetesContractTests.cs`
- Create: `tests/p09/kubernetes-negative.Tests.ps1`

- [ ] **Step 1: Write RED policy tests**

Load every resource JSON with `System.Text.Json` and create one mutation per forbidden case:

```text
Secret, ClusterRole, ClusterRoleBinding, Ingress, LoadBalancer, NodePort,
PersistentVolume, hostPath, hostNetwork, hostPort, privileged,
production namespace, missing nondeployable label, missing default deny,
0.0.0.0/0 egress, app-to-Kafka egress, unscoped Dapr component,
Subscription using publish component, floating image, example.invalid without digest
```

Each mutation must fail with a stable `k8s-*` check ID. Valid base+CI inputs must produce the same canonical object-set SHA across two runs.

- [ ] **Step 2: Confirm RED**

Run the focused .NET and PowerShell tests. Expected: missing assets/validator.

- [ ] **Step 3: Create namespaced JSON resources**

Allowed resource kinds are only Namespace, ServiceAccount, ConfigMap, Deployment, Service, Job, Dapr `Component`, Dapr `Subscription`, and NetworkPolicy. Use JSON for resources and small `kustomization.yaml` files for composition.

Create:

- namespace and four ServiceAccounts;
- Profile ConfigMap containing only canonical non-secret values;
- publisher/receiver Deployments with app + Dapr sidecar annotations;
- non-deploying provisioner Job fixture;
- ClusterIP Services only;
- two Dapr components using `secretKeyRef` names only and exact scopes;
- one Dapr subscription using the subscribe component;
- default-deny ingress/egress policy;
- DNS egress, probe ingress, and sidecar/provisioner-to-Kafka egress policies.

Every CI workload image is `example.invalid/cp6/p09-fixture@sha256:` plus exactly 64 lowercase hex characters and every object has `cp6.io/nondeployable=true`. No Kubernetes Secret object exists.

- [ ] **Step 4: Implement cross-object validation**

`Cp6P09KubernetesValidator.Validate(profile, resources)` parses JSON only and enforces:

- exact namespace and allowed kinds;
- unique `(apiVersion,kind,namespace,name)` identity;
- ServiceAccount references exist;
- Dapr component scopes equal Profile identities;
- subscription Topic/component/route/AppId match Profile;
- every selector resolves to at least one intended workload and no unintended workload;
- default deny exists for both directions;
- permitted policy peers/ports are exact, CIDR `0.0.0.0/0` absent;
- application containers have no Kafka env/address and no Kafka egress selector;
- prohibited fields are absent recursively;
- manifest set contains no string matching secret-value or machine-path patterns.

Return a canonical object-set JSON array sorted by identity and its SHA-256.

- [ ] **Step 5: Implement offline kubectl checks**

`eng/test-p09-kubernetes.ps1` must:

1. validate Profile and source JSON before Docker;
2. run fixed kubectl image with `--network none`, read-only source mount and writable bounded artifact mount;
3. run `kubectl kustomize /workspace/deploy/p09/kubernetes/overlays/ci` twice and compare SHA-256;
4. create an unreachable kubeconfig pointing to `https://127.0.0.1:1`;
5. run `kubectl apply --dry-run=client --validate=false -f $renderedManifest` and require success;
6. prove the command remains successful with network disabled;
7. pass source resource JSON through the C# cross-object validator;
8. write only rendered manifest hash, kubectl version and stable checks to artifacts.

The script must never call `kubectl apply` without `--dry-run=client` and never accept a caller kubeconfig/context.

- [ ] **Step 6: Run mutation, render and contract tests**

```powershell
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09KubernetesContractTests"
pwsh -NoProfile -File tests/p09/kubernetes-negative.Tests.ps1
pwsh -NoProfile -File eng/test-p09-kubernetes.ps1
```

Expected: all pass; two renders have identical SHA; no cluster connection is attempted.

- [ ] **Step 7: Commit**

```powershell
git add deploy/p09/kubernetes eng/test-p09-kubernetes.ps1 src/CP6.Platform.Deployment/Cp6P09KubernetesValidator.cs tests/CP6.Platform.DeploymentTests tests/p09/kubernetes-negative.Tests.ps1
git commit -m "feat: add offline P09 kubernetes contracts"
```

## Task 8: Prove package contents and reproducibility

**Files:**

- Create: `eng/pack-p09.ps1`
- Create: `tests/CP6.Platform.DeploymentTests/P09PackageTests.cs`
- Modify: `.gitignore`

- [ ] **Step 1: Write RED package assertions**

Pack to a test temp directory, open the `.nupkg` as ZIP and assert the exact allowed prefixes:

```text
lib/net8.0/CP6.Platform.Deployment.dll
lib/net8.0/CP6.Platform.Deployment.xml
contracts/p09/
deploy/p09/
README.md
_rels/
package/
[Content_Types].xml
CP6.Platform.Deployment.nuspec
```

Assert all expected contract/deploy files exist; no secret value, machine path, build artifact, evidence output, local secret-store generated file, `.env`, kubeconfig or mutable image tag exists. Assert nuspec dependency groups are empty and version is exactly `0.9.0-alpha.1`.

- [ ] **Step 2: Confirm RED**

Expected: failure because `eng/pack-p09.ps1` and deterministic package checks are absent.

- [ ] **Step 3: Implement exact pack script**

Parameters:

```powershell
param(
  [string]$Version = "0.9.0-alpha.1",
  [string]$OutputPath = "artifacts/p09-package",
  [switch]$VerifyReproducible
)
```

Reject any other version. Run Release build/test, `dotnet pack --no-build -p:PackageVersion=$Version`, scan ZIP entries and extracted text, produce SHA-256. With `VerifyReproducible`, pack twice from the same source/date settings, normalize NuGet ZIP entry timestamps in the comparison helper, and require equal entry-name/content hash maps. Do not publish.

- [ ] **Step 4: Run and commit**

```powershell
pwsh -NoProfile -File eng/pack-p09.ps1 -VerifyReproducible
dotnet test tests/CP6.Platform.DeploymentTests/CP6.Platform.DeploymentTests.csproj --filter "FullyQualifiedName~P09PackageTests"
git add .gitignore eng/pack-p09.ps1 tests/CP6.Platform.DeploymentTests/P09PackageTests.cs
git commit -m "test: verify P09 deployment package contents"
```

## Task 9: Integrate proportional CI and repository verification

**Files:**

- Modify: `eng/verify.ps1`
- Modify: `.github/workflows/platform-validation.yml`
- Modify: `TESTING.md`
- Create: `tests/CP6.Platform.DeploymentTests/P09WorkflowContractTests.cs`

- [ ] **Step 1: Write RED workflow contract tests**

Assert workflow has a distinct Ubuntu P09 job that runs Deployment tests, Kubernetes static gate and real Compose runner. Assert it uploads only `artifacts/p09-rehearsal/**` and `artifacts/p09-kubernetes/**`, fails on runner failure, has a finite timeout, and never has cloud credentials, production environments, Registry login, deploy command or continue-on-error.

Assert Windows/Linux existing jobs run the new Deployment tests but do not require Docker rehearsal. Assert P05/P06/P08 jobs remain present and unchanged in meaning.

- [ ] **Step 2: Confirm RED**

Run the workflow contract filter. Expected: missing P09 job/profile.

- [ ] **Step 3: Add verification profiles**

Extend `eng/verify.ps1` with explicit opt-in gates:

- `P09Contract`: Deployment tests + script tests + Kubernetes offline gate;
- `P09Real`: `P09Contract` then real Compose rehearsal.

Default local verification runs `P09Contract` only when Docker/kubectl container is available; absence returns a visible NotRun and never a false pass. CI calls exact switches so the Ubuntu P09 job cannot skip.

- [ ] **Step 4: Add the Ubuntu runtime job**

Job name: `ubuntu-p09-non-production-runtime`. Use existing pinned checkout/setup-dotnet actions, Ubuntu runner, 30-minute timeout, no environment/permissions beyond `contents: read`. Steps:

1. checkout exact commit;
2. restore/build;
3. run Deployment and repository architecture tests;
4. run PowerShell script unit tests;
5. run offline Kubernetes gate;
6. run real Compose rehearsal with `${{ github.sha }}`;
7. validate evidence Schema/hash/zero residue;
8. secret-pattern scan artifacts;
9. upload bounded artifacts on success or failure.

Do not introduce a second reviewer, cloud job, long soak, production approval or deployment step.

- [ ] **Step 5: Run all non-runtime gates locally**

```powershell
dotnet format CP6.Platform.sln --verify-no-changes
dotnet build CP6.Platform.sln -c Release
dotnet test CP6.Platform.sln -c Release --no-build
pwsh -NoProfile -File tests/p09/compose-rehearsal.Tests.ps1
pwsh -NoProfile -File tests/p09/cleanup-failure.Tests.ps1
pwsh -NoProfile -File tests/p09/kubernetes-negative.Tests.ps1
pwsh -NoProfile -File eng/test-p09-kubernetes.ps1
pwsh -NoProfile -File eng/pack-p09.ps1 -VerifyReproducible
```

Expected: all available gates pass, zero skipped contract tests. Docker absence may only affect the real Compose command, not Schema/static/package gates.

- [ ] **Step 6: Commit**

```powershell
git add .github/workflows/platform-validation.yml eng/verify.ps1 TESTING.md tests/CP6.Platform.DeploymentTests/P09WorkflowContractTests.cs
git commit -m "ci: gate P09 non-production runtime"
```

## Task 10: Document the candidate state and complete branch evidence

**Files:**

- Create: `docs/P09-NON-PRODUCTION-RUNTIME.md`
- Create: `docs/P09-PUBLICATION.md`
- Modify: `README.md`
- Modify: `VERSION`
- Modify: `CHANGELOG-AI.md`
- Modify: `docs/project-memory/PROJECT_STATE.md`
- Modify: `docs/project-memory/05-Completed.md`
- Modify: `docs/project-memory/06-Todo.md`
- Create: `tests/CP6.Platform.DeploymentTests/P09DocumentationContractTests.cs`

- [ ] **Step 1: Write RED documentation state tests**

Assert docs contain exact status `Implemented / Rehearsal Candidate`, version values, scope/non-goals, runner commands, evidence fields, cleanup semantics and the S04-S06 next sequence. Reject `Frozen / Consumable`, published package claims, CRM route/Worker/business Topic, real cluster/cloud/deployment claims, production readiness and multi-person approval requirements.

- [ ] **Step 2: Confirm RED**

Run the documentation contract filter. Expected: files/status absent.

- [ ] **Step 3: Write operator and consumer documentation**

`P09-NON-PRODUCTION-RUNTIME.md` documents prerequisites, exact local commands, expected Passed/NotRun/Failed outcomes, artifact locations, safe cleanup, threat boundary and troubleshooting without secret examples.

`P09-PUBLICATION.md` is explicitly a publication prerequisite/runbook, not evidence that publication occurred. It records that S04 must start from exact `origin/main`, publish only `CP6.Platform.Deployment 0.9.0-alpha.1`, reject overwrite, rerun P05/P06/P08/P09 gates, and retain package/evidence hashes.

- [ ] **Step 4: Update project state without overclaiming**

Set `VERSION` to `0.9.0.0`. Record S01-S03 complete on the implementation branch only after tests pass. Keep P09 publication/CRM/public sync/final audit in `06-Todo.md`; do not mark them complete. Existing P08 history remains immutable.

- [ ] **Step 5: Run the complete proportional verification**

```powershell
dotnet format CP6.Platform.sln --verify-no-changes
dotnet build CP6.Platform.sln -c Release
dotnet test CP6.Platform.sln -c Release --no-build
pwsh -NoProfile -File eng/verify.ps1 -P09Contract
pwsh -NoProfile -File eng/pack-p09.ps1 -VerifyReproducible
git diff --check
git status --short
git diff origin/main...HEAD --stat
git diff origin/main...HEAD
```

If local Docker is available, also run:

```powershell
pwsh -NoProfile -File eng/verify.ps1 -P09Real -ExpectedGitSha (git rev-parse HEAD)
```

Expected: all mandatory local static/package gates pass; diff contains only P09 S01-S03; no secrets/machine paths/debug residue; real Compose is Passed locally or left for the mandatory same-SHA Ubuntu job.

- [ ] **Step 6: Commit documentation and state**

```powershell
git add README.md VERSION CHANGELOG-AI.md docs/P09-NON-PRODUCTION-RUNTIME.md docs/P09-PUBLICATION.md docs/project-memory/PROJECT_STATE.md docs/project-memory/05-Completed.md docs/project-memory/06-Todo.md tests/CP6.Platform.DeploymentTests/P09DocumentationContractTests.cs
git commit -m "docs: record P09 rehearsal candidate"
```

- [ ] **Step 7: Rebase or merge the latest remote baseline safely**

Fetch first. If the branch has already been pushed/shared, merge `origin/main`; otherwise a normal rebase is allowed. Never rewrite shared history.

```powershell
git fetch origin
git status --short
git merge --no-edit origin/main
```

Expected: clean merge or conflicts resolved only within P09 files; rerun the full proportional verification after any baseline change.

- [ ] **Step 8: Push and open the implementation PR**

```powershell
git push -u origin codex/p09-nonprod-runtime-implementation
$implementationPrUrl = gh pr create --base main --head codex/p09-nonprod-runtime-implementation --title "feat: add P09 non-production runtime rehearsal" --body "Implements P09-S01 through P09-S03 only: strict Profile/evidence contracts, independent Deployment package, real Dapr/Kafka Compose rehearsal, Kubernetes offline validation, content-addressed evidence, and zero-residue cleanup. Candidate state is Implemented / Rehearsal Candidate. Package publication, CRM consumption, public sync, real cloud/cluster and production deployment remain excluded."
$implementationPrNumber = gh pr view $implementationPrUrl --json number --jq .number
```

After PR creation, edit its body once evidence exists so it lists the exact commit, Profile/package versions, local gates, Ubuntu P09 job name, evidence artifact/hash, risk boundary and S04-S06 exclusions. Do not request a second reviewer; self-review plus automated gates is sufficient for this solo repository.

- [ ] **Step 9: Require PR-head and merge-commit evidence**

Wait for all required checks. The P09 Ubuntu job must be Passed for the PR head. Merge without bypassing checks, then verify exact remote main:

```powershell
gh pr checks $implementationPrNumber --watch
gh pr merge $implementationPrNumber --merge
git fetch origin
git rev-parse origin/main
gh run list --branch main --limit 10
```

Run/inspect the exact `origin/main` workflow and require `ubuntu-p09-non-production-runtime` Passed with matching evidence hash. If repository protection does not trigger `main` automatically, manually dispatch the existing validation workflow against exact main; do not weaken the gate.

- [ ] **Step 10: Stop at the candidate boundary**

Final state after this plan:

```text
P09-S01: complete
P09-S02: complete
P09-S03: complete
P09: Implemented / Rehearsal Candidate
P09-S04/S05/S06: not started
```

Only then create the separate S04 publication plan. Do not publish, edit CRM/public repositories, deploy, delete remote branches or claim `Frozen / Consumable` in this plan.

## Plan self-review checklist

- [ ] Every design goal for S01-S03 maps to a task and automated assertion.
- [ ] Every negative boundary has a stable test ID and fails before side effects where applicable.
- [ ] Compose credentials are generated only in bounded OS temp storage and never enter package/artifacts.
- [ ] Kubernetes assets contain no Secret object and require no cluster/network.
- [ ] Existing P05/P06/P08 packages, workflows and identities remain authoritative and unmodified in meaning.
- [ ] Package is independent and only `CP6.Platform.Deployment` has version `0.9.0-alpha.1`.
- [ ] Runtime/package/evidence types and Schema field names match exactly.
- [ ] Implementation files/tests/scripts do not assume a user name, drive letter or host-specific configuration; Task 0's absolute worktree path is only this workspace's orchestration command.
- [ ] No step introduces CRM route/Worker/business Topic, P10 candidate semantics, cloud or production deployment.
- [ ] Solo gates keep Critical/High/secrets/network/hash/residue at zero tolerance without adding team-only approval ceremony.
- [ ] Completion language stops at `Implemented / Rehearsal Candidate`.
