# P08 Observability, Health, Resilience, and SLO Evidence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver P08-S01 in `CP6.Platform`: exporter-neutral OpenTelemetry registration, safe operational endpoints, fail-closed HTTP resilience, messaging/EF instrumentation, deterministic test utilities, cross-service trace/fault evidence, versioned SLO evidence contracts, and runbooks without provisioning an observability backend or a production route.

**Architecture:** Extend the existing five production packages without adding a sixth runtime package. Contracts owns release/SLO wire shapes, Abstractions owns stable telemetry names and the release accessor, AspNetCore composes official OpenTelemetry and HTTP resilience libraries, Messaging and EntityFramework emit activities/metrics without changing business semantics, and repository-only Testing captures evidence and injects deterministic faults. Exporters, environment dependencies, dashboards, alerts, CRM runtime wiring, publication, and deployment remain outside S01.

**Tech Stack:** .NET 8/C# 12, ASP.NET Core health checks, OpenTelemetry .NET `1.18.0`, `Microsoft.Extensions.Http.Resilience` `10.9.0`, CloudNative.CloudEvents `2.9.0`, Dapr.Client `1.18.5`, EF Core `8.0.30`, JsonSchema.Net `9.4.0`, xUnit `2.9.3`, PowerShell verification gates, GitHub Actions.

---

## Execution boundary

This plan implements only **P08-S01**. It must start after the approved S00 design and this plan are merged to Platform `main`, on a new branch/worktree created from the fetched `origin/main`:

```powershell
git -C D:\CP6\CP6.Platform fetch origin
git -C D:\CP6\CP6.Platform rev-parse main
git -C D:\CP6\CP6.Platform rev-parse origin/main
git -C D:\CP6\CP6.Platform worktree add -b codex/p08-s01-observability-resilience D:\CP6-worktrees\p08-s01-observability-resilience origin/main
git -C D:\CP6-worktrees\p08-s01-observability-resilience status --short --branch
```

The two `rev-parse` results must match before implementation. If they do not, fast-forward the local `main` reference without touching the dirty root worktree, then create the S01 worktree. Do not reuse the S00 branch for implementation and do not clean or modify `D:\CP6` user changes.

After S01 is merged and all Platform main gates pass, create separate plans/branches for:

1. P08-S02 exact-main immutable `0.8.0-alpha.1` publication;
2. P08-S03 CRM fixed-version black-box consumption;
3. P08-S04 CRM locator freeze;
4. P08-S05 public project-memory update;
5. P08-S06 Platform final evidence freeze.

P08 is not `Frozen / Consumable` until all seven stages S00–S06 complete.

## Frozen public surface

Use these exact production types and keep every public type documented:

| Package | Public surface introduced by S01 |
| --- | --- |
| Contracts | `Cp6ReleaseIdentity`, `Cp6ReleaseMode`, `Cp6SloEvidenceDocument`, SLO enums/value records, `Cp6SloEvidenceEvaluator` |
| Abstractions | `ICp6ReleaseIdentityAccessor`, `Cp6TelemetryConventions`, `Cp6TelemetrySources`, `Cp6TelemetryMeters`, `Cp6HealthTags` |
| AspNetCore | `Cp6ObservabilityProfile`, `AddCp6Observability`, `Cp6OperationalEndpointProfile`, `MapCp6OperationalEndpoints`, `Cp6HttpOperationKind`, `Cp6HttpResilienceProfile`, `Cp6HttpFailureCategory`, `Cp6HttpResilienceException`, `AddCp6HttpResilience` |
| Messaging | optional CloudEvent `traceparent`/`tracestate`, `Cp6TraceContextCodec`, activities and low-cardinality metrics around Dapr/Kafka paths |
| EntityFramework | activities and low-cardinality metrics around existing Outbox/Inbox paths |
| Testing (repository-only) | `Cp6TelemetryRecorder`, `Cp6HttpFaultScript`, `Cp6HttpFaultHandler`, Test/CI-only registration |

The release mode and validator contract is:

```csharp
namespace CP6.Platform.Contracts;

public enum Cp6ReleaseMode
{
    NonCandidate,
    Candidate
}

public sealed record Cp6ReleaseIdentity(
    string Service,
    string Version,
    string GitSha,
    string ArtifactDigest,
    string ContractBundleDigest,
    Cp6ReleaseMode Mode)
{
    public bool Candidate => Mode == Cp6ReleaseMode.Candidate;

    public void Validate();
}
```

`Candidate` validation requires a lowercase service name, SemVer without build metadata, exactly 40 lowercase hexadecimal Git SHA characters, and lowercase `sha256:` plus 64 hexadecimal characters for both digests. `NonCandidate` requires a lowercase service name and non-empty version, but its SHA/digest fields may be empty. No `NonCandidate` identity can produce `Pass` SLO evidence.

The composition APIs are:

```csharp
namespace Microsoft.Extensions.DependencyInjection;

public static class Cp6ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddCp6Observability(
        this IServiceCollection services,
        Cp6ObservabilityProfile profile);
}

public static class Cp6HttpResilienceServiceCollectionExtensions
{
    public static IHttpClientBuilder AddCp6HttpResilience(
        this IHttpClientBuilder builder,
        Cp6HttpResilienceProfile profile);
}

namespace Microsoft.AspNetCore.Routing;

public static class Cp6OperationalEndpointRouteBuilderExtensions
{
    public static IEndpointConventionBuilder MapCp6OperationalEndpoints(
        this IEndpointRouteBuilder endpoints,
        Cp6OperationalEndpointProfile profile);
}
```

Profiles are immutable records. Constructors validate all required values; repeated registration with an equal profile is idempotent, while a second unequal profile throws `InvalidOperationException` during service registration or startup.

## Stable telemetry contract

Activity sources:

- `CP6.Platform.AspNetCore`
- `CP6.Platform.Messaging`
- `CP6.Platform.EntityFramework`

Meter names use the same three names. Stable operation names are:

- `cp6.http.outbound`
- `cp6.messaging.dapr.invoke`
- `cp6.messaging.publish`
- `cp6.messaging.consume`
- `cp6.outbox.dispatch`
- `cp6.inbox.process`

Only these CP6 tags are permitted by default:

- `cp6.region`
- `cp6.operation`
- `cp6.outcome`
- `cp6.error.code`
- `cp6.messaging.transport`
- `cp6.messaging.disposition`
- `cp6.http.operation_kind`

Never add correlation, event, trace, user, organization, tenant, aggregate/resource, URL/query, host, topic/ACL, payload/body, exception-message, token, cookie, or connection-string values to metric labels. Release Git SHA and digests are resource/evidence attributes only, never high-frequency metric labels.

## Task 1: Freeze dependencies, package version, and architecture boundary

**Files:**

- Modify: `Directory.Build.props`
- Modify: `Directory.Packages.props`
- Modify: `src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj`
- Modify: `src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`
- Test: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Write the failing architecture/version test**

Add assertions that `VersionPrefix` is `0.8.0`, the four dependency versions are exact, AspNetCore references only the approved OTel/resilience packages in addition to its current packages, Messaging references Abstractions, and no production project references `CP6.Platform.Testing`.

```csharp
[Fact]
public void P08_DependencyAndPackageBoundary_IsExact()
{
    AssertCentralVersion("OpenTelemetry.Extensions.Hosting", "1.18.0");
    AssertCentralVersion("OpenTelemetry.Instrumentation.AspNetCore", "1.18.0");
    AssertCentralVersion("OpenTelemetry.Instrumentation.Http", "1.18.0");
    AssertCentralVersion("Microsoft.Extensions.Http.Resilience", "10.9.0");
    AssertVersionPrefix("0.8.0");
    AssertNoProductionReference("CP6.Platform.Testing");
}
```

- [ ] **Step 2: Run the focused test and confirm RED**

Run:

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P08_DependencyAndPackageBoundary_IsExact
```

Expected: failure because P08 package versions/references do not exist and `VersionPrefix` is still `0.7.0`.

- [ ] **Step 3: Add exact package references and version**

Add central versions and these AspNetCore references:

```xml
<PackageReference Include="Microsoft.Extensions.Http.Resilience" />
<PackageReference Include="OpenTelemetry.Extensions.Hosting" />
<PackageReference Include="OpenTelemetry.Instrumentation.AspNetCore" />
<PackageReference Include="OpenTelemetry.Instrumentation.Http" />
```

Add this Messaging reference:

```xml
<ProjectReference Include="../CP6.Platform.Abstractions/CP6.Platform.Abstractions.csproj" />
```

Update the architecture allowlist rather than disabling dependency checks.

- [ ] **Step 4: Restore and confirm GREEN**

```powershell
dotnet restore CP6.Platform.sln
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P08_DependencyAndPackageBoundary_IsExact
```

Expected: both commands exit 0.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Build.props Directory.Packages.props src/CP6.Platform.AspNetCore/CP6.Platform.AspNetCore.csproj src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git commit -m "build(platform): freeze P08 observability dependencies"
```

## Task 2: Add release identity and stable telemetry conventions

**Files:**

- Create: `src/CP6.Platform.Contracts/Cp6ReleaseIdentity.cs`
- Create: `src/CP6.Platform.Abstractions/ICp6ReleaseIdentityAccessor.cs`
- Create: `src/CP6.Platform.Abstractions/Cp6TelemetryConventions.cs`
- Test: `tests/CP6.Platform.UnitTests/ReleaseIdentityTests.cs`
- Test: `tests/CP6.Platform.UnitTests/TelemetryConventionTests.cs`

- [ ] **Step 1: Write release validation and convention tests**

Cover a valid Candidate, uppercase/short SHA, non-SemVer version, malformed digest, safe NonCandidate, all source/meter names, the exact tag allowlist, and rejection of forbidden/high-cardinality names.

```csharp
[Fact]
public void Candidate_RequiresImmutableIdentity()
{
    var identity = new Cp6ReleaseIdentity(
        "crm-api",
        "0.8.0-alpha.1",
        new string('a', 40),
        $"sha256:{new string('b', 64)}",
        $"sha256:{new string('c', 64)}",
        Cp6ReleaseMode.Candidate);

    identity.Validate();
    Assert.True(identity.Candidate);
}

[Fact]
public void MetricTags_ExcludeHighCardinalityValues()
{
    Assert.DoesNotContain("tenant.id", Cp6TelemetryConventions.AllowedMetricTags);
    Assert.DoesNotContain("correlation.id", Cp6TelemetryConventions.AllowedMetricTags);
    Assert.DoesNotContain("trace.id", Cp6TelemetryConventions.AllowedMetricTags);
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ReleaseIdentityTests|FullyQualifiedName~TelemetryConventionTests"
```

Expected: compile failure because the public types do not exist.

- [ ] **Step 3: Implement the contracts**

Use generated, culture-invariant regexes and throw `ArgumentException`/`ArgumentOutOfRangeException` without echoing rejected values. Define the accessor exactly as:

```csharp
using CP6.Platform.Contracts;

namespace CP6.Platform.Abstractions;

public interface ICp6ReleaseIdentityAccessor
{
    Cp6ReleaseIdentity Current { get; }
}
```

Expose source/meter/tag collections as immutable `IReadOnlySet<string>`/`IReadOnlyList<string>` and provide `EnsureAllowedMetricTag(string name)` that throws without rendering a value.

- [ ] **Step 4: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~ReleaseIdentityTests|FullyQualifiedName~TelemetryConventionTests"
```

Expected: all focused tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.Contracts/Cp6ReleaseIdentity.cs src/CP6.Platform.Abstractions/ICp6ReleaseIdentityAccessor.cs src/CP6.Platform.Abstractions/Cp6TelemetryConventions.cs tests/CP6.Platform.UnitTests/ReleaseIdentityTests.cs tests/CP6.Platform.UnitTests/TelemetryConventionTests.cs
git commit -m "feat(platform): add release and telemetry contracts"
```

## Task 3: Add Draft 2020-12 SLO evidence contract and packaged fixtures

**Files:**

- Create: `src/CP6.Platform.Contracts/Cp6SloEvidenceDocument.cs`
- Create: `src/CP6.Platform.Contracts/Cp6SloEvidenceEvaluator.cs`
- Modify: `src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj`
- Modify: `src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj`
- Create: `contracts/observability/slo-evidence/v1/schema.json`
- Create: `contracts/observability/slo-evidence/v1/examples/valid-pass.json`
- Create: `contracts/observability/slo-evidence/v1/examples/partial-indeterminate.json`
- Create: `contracts/observability/slo-evidence/v1/examples/non-candidate-indeterminate.json`
- Create: `contracts/observability/slo-evidence/v1/examples/pii-negative.json`
- Test: `tests/CP6.Platform.UnitTests/SloEvidenceContractTests.cs`

- [ ] **Step 1: Write schema and evaluator tests**

Tests must load the repository schema with JsonSchema.Net, validate all four fixtures, reject duplicate JSON properties before schema evaluation, verify fixture SHA drift is detectable, and exercise the decision matrix:

```csharp
[Theory]
[InlineData(true, Cp6SloEvidenceCompleteness.Complete, true, Cp6SloEvidenceResult.Pass)]
[InlineData(false, Cp6SloEvidenceCompleteness.Complete, true, Cp6SloEvidenceResult.Indeterminate)]
[InlineData(true, Cp6SloEvidenceCompleteness.Partial, true, Cp6SloEvidenceResult.Indeterminate)]
[InlineData(true, Cp6SloEvidenceCompleteness.Complete, false, Cp6SloEvidenceResult.Fail)]
public void Evaluate_UsesFailClosedMatrix(
    bool candidate,
    Cp6SloEvidenceCompleteness completeness,
    bool thresholdMet,
    Cp6SloEvidenceResult expected)
{
    var evidence = EvidenceFixture.Create(candidate, completeness, thresholdMet);
    Assert.Equal(expected, Cp6SloEvidenceEvaluator.Evaluate(evidence));
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter FullyQualifiedName~SloEvidenceContractTests
```

Expected: compile/file-not-found failure.

- [ ] **Step 3: Implement the wire model and evaluator**

Use string enum serialization with exact wire values `Complete`, `Partial`, `Missing`, `Pass`, `Fail`, `Indeterminate`. The schema ID is `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json`, `schemaVersion` is `1.0.0`, `additionalProperties` is false at every object level, timestamps require UTC `Z`, and all digests use lowercase `sha256:` format. `sli` includes `definitionDigest`; every source includes `definitionDigest`, `releaseArtifactDigest`, `queryDefinitionDigest`, `evidenceArtifactDigest`, and `exclusionsVerified`, which makes definition drift, release drift, artifact drift, and exclusions auditable. Every example contains `productionSloClaimed: false`.

Evaluator order is exact:

```csharp
public static Cp6SloEvidenceResult Evaluate(Cp6SloEvidenceDocument evidence)
{
    ArgumentNullException.ThrowIfNull(evidence);
    evidence.Validate();

    if (!evidence.Release.Candidate ||
        evidence.Completeness != Cp6SloEvidenceCompleteness.Complete ||
        evidence.Window.ExpectedCoverage != evidence.Window.ObservedCoverage ||
        evidence.Measurement.SampleCount == 0 ||
        evidence.Sources.Count == 0 ||
        evidence.Sources.Any(source =>
            !source.HasValidDigests ||
            source.ReleaseArtifactDigest != evidence.Release.ArtifactDigest ||
            source.DefinitionDigest != evidence.Sli.DefinitionDigest) ||
        (evidence.Measurement.ExcludedCount > 0 &&
            evidence.Sources.Any(source => !source.ExclusionsVerified)))
    {
        return Cp6SloEvidenceResult.Indeterminate;
    }

    return evidence.Measurement.Meets(evidence.Sli)
        ? Cp6SloEvidenceResult.Pass
        : Cp6SloEvidenceResult.Fail;
}
```

- [ ] **Step 4: Split package assets deliberately**

Contracts packs only `contracts/observability/**/*`. Messaging must stop using the repository-wide `contracts/**/*` glob and instead pack only `contract-bundle.v1.json` plus `contracts/events/**/*`; this prevents the SLO schema from being duplicated into Messaging.

- [ ] **Step 5: Run focused tests and pack inspection**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter FullyQualifiedName~SloEvidenceContractTests
dotnet pack src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj --configuration Release --output artifacts/p08-contract-check -p:Version=0.8.0-alpha.1
```

Expected: tests pass and `CP6.Platform.Contracts.0.8.0-alpha.1.nupkg` contains the schema plus all four fixtures under `contracts/observability/slo-evidence/v1/`.

- [ ] **Step 6: Commit**

```powershell
git add src/CP6.Platform.Contracts/Cp6SloEvidenceDocument.cs src/CP6.Platform.Contracts/Cp6SloEvidenceEvaluator.cs src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj src/CP6.Platform.Messaging/CP6.Platform.Messaging.csproj contracts/observability tests/CP6.Platform.UnitTests/SloEvidenceContractTests.cs
git commit -m "feat(platform): add SLO evidence contract"
```

## Task 4: Compose exporter-neutral OpenTelemetry registration

**Files:**

- Create: `src/CP6.Platform.AspNetCore/Cp6ObservabilityProfile.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6ReleaseIdentityAccessor.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs`

- [ ] **Step 1: Write registration tests**

Cover valid Candidate and NonCandidate profiles, missing/invalid Candidate identity, equal repeat registration, unequal repeat registration, resource attributes, exact source/meter subscriptions, W3C trace-only propagation with no baggage, no exporter by default, and business requests succeeding when no exporter exists.

```csharp
[Fact]
public void AddCp6Observability_RejectsProfileDrift()
{
    var services = new ServiceCollection();
    services.AddCp6Observability(Profile("service-a"));

    Assert.Throws<InvalidOperationException>(
        () => services.AddCp6Observability(Profile("service-b")));
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~ObservabilityRegistrationTests
```

Expected: compile failure.

- [ ] **Step 3: Implement the profile and registration**

The profile contains `ServiceName`, `ServiceVersion`, `EnvironmentName`, `Region`, and `Cp6ReleaseIdentity`. Validate lowercase bounded service/environment/region values and require service/version consistency with the release identity.

Compose the official SDK without exporter registration:

```csharp
services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(profile.ServiceName, serviceVersion: profile.ServiceVersion)
        .AddAttributes(new Dictionary<string, object>
        {
            ["deployment.environment.name"] = profile.EnvironmentName,
            ["cp6.region"] = profile.Region,
            ["cp6.release.git_sha"] = profile.ReleaseIdentity.GitSha,
            ["cp6.release.artifact_digest"] = profile.ReleaseIdentity.ArtifactDigest,
            ["cp6.release.contract_bundle_digest"] = profile.ReleaseIdentity.ContractBundleDigest,
            ["cp6.release.candidate"] = profile.ReleaseIdentity.Candidate
        }))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddSource(Cp6TelemetrySources.All.ToArray()))
    .WithMetrics(metrics => metrics.AddMeter(Cp6TelemetryMeters.All.ToArray()));
```

Before composing providers, set the OpenTelemetry default text-map propagator to a single `TraceContextPropagator`; an equal repeated registration is allowed, while a pre-existing incompatible CP6 registration fails rather than silently enabling baggage. Add Git SHA/digest resource attributes only when non-empty, so NonCandidate local runs do not emit empty identity attributes.

Register a singleton accessor and an internal registration marker carrying the immutable profile. Do not configure OTLP, console, Azure Monitor, baggage, sampling backend, or endpoints.

- [ ] **Step 4: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~ObservabilityRegistrationTests
```

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.AspNetCore/Cp6ObservabilityProfile.cs src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs src/CP6.Platform.AspNetCore/Cp6ReleaseIdentityAccessor.cs tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs
git commit -m "feat(platform): compose exporter-neutral telemetry"
```

## Task 5: Add safe live/startup/ready/release endpoints

**Files:**

- Create: `src/CP6.Platform.AspNetCore/Cp6OperationalEndpointProfile.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6OperationalEndpointRouteBuilderExtensions.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6SafeHealthResponseWriter.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/OperationalEndpointTests.cs`

- [ ] **Step 1: Write integration tests with `WebApplication`**

Register tagged checks with both approved and unsafe names plus secret-bearing `HealthCheckResult.Data`, then assert:

- live runs no external check and returns 200;
- startup/ready run only matching tags and any non-Healthy maps to 503;
- release returns 200 only for complete Candidate or explicit safe NonCandidate identity, but returns 503 for invalid/drifted identity;
- all bodies contain only `schemaVersion`, `status`, `observedAtUtc`, explicitly allowlisted stable component name/status, and safe release fields;
- unlisted or syntactically unsafe component names can affect aggregate status but are omitted from the body;
- every response contains `Cache-Control: no-store` and excludes exception text, duration, data dictionary, host/database/topic/tenant/secret values;
- mapping an equal profile twice is idempotent; mapping different paths/profile fails startup.

```csharp
[Theory]
[InlineData("/health/startup", "startup")]
[InlineData("/health/ready", "ready")]
public async Task DependencyEndpoints_RedactHealthData(string path, string tag)
{
    await using var app = await OperationalHost.StartAsync(tag, "connection-string-must-not-appear");
    var response = await app.Client.GetAsync(path);
    var body = await response.Content.ReadAsStringAsync();

    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.Contains("no-store", response.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
    Assert.DoesNotContain("connection-string-must-not-appear", body, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~OperationalEndpointTests
```

- [ ] **Step 3: Implement endpoint profiles and writer**

Use exact default paths `/health/live`, `/health/startup`, `/health/ready`, `/health/release`. `live` uses a constant in-process check only. `startup` and `ready` use `HealthCheckOptions.Predicate` against `Cp6HealthTags.Startup`/`Ready`. `Cp6OperationalEndpointProfile` carries an immutable set of published component names validated against `^[a-z][a-z0-9.-]{0,63}$`; checks outside this explicit set still determine aggregate status but are not rendered. The safe writer sorts allowed entries by name, maps both `Degraded` and `Unhealthy` to 503 for startup/ready, writes JSON through `Utf8JsonWriter`, and never serializes `Description`, `Exception`, `Duration`, or `Data`.

- [ ] **Step 4: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~OperationalEndpointTests
```

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.AspNetCore/Cp6OperationalEndpointProfile.cs src/CP6.Platform.AspNetCore/Cp6OperationalEndpointRouteBuilderExtensions.cs src/CP6.Platform.AspNetCore/Cp6SafeHealthResponseWriter.cs tests/CP6.Platform.AspNetCoreTests/OperationalEndpointTests.cs
git commit -m "feat(platform): add safe operational endpoints"
```

## Task 6: Add fail-closed outbound correlation and HTTP resilience

**Files:**

- Create: `src/CP6.Platform.AspNetCore/Cp6HttpOperationKind.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6HttpResilienceProfile.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6CorrelationId.cs`
- Modify: `src/CP6.Platform.AspNetCore/Cp6CorrelationMiddleware.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6OutboundCorrelationHandler.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6HttpFailureCategory.cs`
- Create: `src/CP6.Platform.AspNetCore/Cp6HttpResilienceException.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/HttpResilienceTests.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/OutboundCorrelationTests.cs`

- [ ] **Step 1: Write profile/correlation/fault tests**

Use deterministic handlers, not wall-clock sleeps. Cover profile bounds, unknown operation registration, GET/HEAD/OPTIONS method enforcement, idempotent write with/without one stable `Idempotency-Key`, non-idempotent one attempt, approved transient exceptions/statuses only, exact retry count, attempt and total timeout categories, circuit open/half-open/recovery, immediate caller cancellation, no hedging/fallback, equal repeat registration, and conflicting correlation header replacement.

```csharp
[Fact]
public async Task IdempotentWrite_WithoutStableKey_FailsBeforeNetwork()
{
    var transport = new CountingHandler(HttpStatusCode.OK);
    var client = ClientFactory.Create(Cp6HttpOperationKind.IdempotentWrite, transport);

    var exception = await Assert.ThrowsAsync<Cp6HttpResilienceException>(
        () => client.PostAsync("/orders", new StringContent("{}")));

    Assert.Equal(Cp6HttpFailureCategory.IdempotencyRequired, exception.Category);
    Assert.Equal(0, transport.Attempts);
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter "FullyQualifiedName~HttpResilienceTests|FullyQualifiedName~OutboundCorrelationTests"
```

- [ ] **Step 3: Implement profile bounds and operation predicate**

The immutable profile requires a non-empty client name and explicit operation kind. Bounds are: retry attempts `0..5`, attempt timeout `100 ms..30 s`, total timeout `250 ms..120 s`, circuit sampling `1 s..120 s`, minimum throughput `2..1000`, break duration `1 s..300 s`. `NonIdempotent` forces retry attempts to zero. Approved retry outcomes are `HttpRequestException`, HTTP 408, 429, and 500/502/503/504; caller cancellation is never handled.

Use `AddResilienceHandler` with total timeout, retry, circuit breaker, and attempt timeout in that order. Do not add hedging or fallback. The request predicate must reject wrong HTTP methods and missing/duplicate/invalid idempotency keys before calling the inner handler.

- [ ] **Step 4: Implement safe outbound correlation**

Extract the P03 validation/generation rule into internal `Cp6CorrelationId` and make `Cp6CorrelationMiddleware` use it without changing P03 behavior. The outbound handler removes every existing `X-Correlation-Id`, selects the already validated `HttpContext.TraceIdentifier`, else `IRequestContext.CorrelationId`, else a new lowercase GUID, validates it through the same helper, and adds exactly one header. It never derives correlation from `Activity.TraceId` or an identity header.

- [ ] **Step 5: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter "FullyQualifiedName~HttpResilienceTests|FullyQualifiedName~OutboundCorrelationTests"
```

- [ ] **Step 6: Commit**

```powershell
git add src/CP6.Platform.AspNetCore/Cp6HttpOperationKind.cs src/CP6.Platform.AspNetCore/Cp6HttpResilienceProfile.cs src/CP6.Platform.AspNetCore/Cp6HttpResilienceServiceCollectionExtensions.cs src/CP6.Platform.AspNetCore/Cp6CorrelationId.cs src/CP6.Platform.AspNetCore/Cp6CorrelationMiddleware.cs src/CP6.Platform.AspNetCore/Cp6OutboundCorrelationHandler.cs src/CP6.Platform.AspNetCore/Cp6HttpFailureCategory.cs src/CP6.Platform.AspNetCore/Cp6HttpResilienceException.cs tests/CP6.Platform.AspNetCoreTests/HttpResilienceTests.cs tests/CP6.Platform.AspNetCoreTests/OutboundCorrelationTests.cs
git commit -m "feat(platform): add fail-closed HTTP resilience"
```

## Task 7: Propagate optional W3C context through CloudEvents without weakening P04/P05

**Files:**

- Modify: `src/CP6.Platform.Messaging/Cp6CloudEventAttributes.cs`
- Modify: `src/CP6.Platform.Messaging/Cp6CloudEventCodec.cs`
- Create: `src/CP6.Platform.Messaging/Cp6TraceContextCodec.cs`
- Modify: `src/CP6.Platform.Messaging/Cp6DaprDeliveryValidator.cs`
- Modify: `tests/CP6.Platform.UnitTests/CloudEventContractTests.cs`
- Modify: `tests/CP6.Platform.UnitTests/DaprKafkaContractTests.cs`
- Create: `tests/CP6.Platform.UnitTests/TraceContextContractTests.cs`

- [ ] **Step 1: Write trace propagation and compatibility tests**

Cover valid inject/extract, no current activity, optional tracestate, no baggage, malformed/duplicate/overlong fields, remote flag, invalid context returning `null`, a new root activity for a valid business message with invalid trace context, and preservation of the P04 seven-required-attribute contract.

```csharp
[Fact]
public void RequiredAttributes_RemainTheOriginalSeven()
{
    Assert.Equal(
        new[] { "tenantid", "correlationid", "causationid", "aggregateid", "aggregateversion", "schemaversion", "region" },
        Cp6CloudEventAttributes.Required.Select(attribute => attribute.Name));
    Assert.Equal(9, Cp6CloudEventAttributes.All.Count);
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~DaprKafkaContractTests|FullyQualifiedName~TraceContextContractTests"
```

- [ ] **Step 3: Implement attributes and codec**

Add `TraceParent` and `TraceState` extension definitions; expose `Required` as the original seven and `All` as nine. `ValidateEnvelope` iterates `Required`, not `All`. `Cp6CloudEventCodec.Create` gains an overload accepting `ActivityContext?`; the existing overload captures `Activity.Current?.Context` and delegates. Injection uses W3C trace formatting and emits no baggage.

`Cp6TraceContextCodec.TryExtract` runs against the structured JSON telemetry fields before P04 schema and P05 topic/key decisions, returns a nullable remote `ActivityContext`, rejects duplicate JSON properties/multiple values, caps `traceparent` at 55 and `tracestate` at 512 characters, never echoes rejected input, and increments the stable `cp6.messaging.trace_context.rejected` counter with only `cp6.error.code=invalid_trace_context`. Its result is attached only after business validation succeeds; invalid telemetry never changes the business-valid result and invalid business data never reaches the handler.

Add a non-positional init-only property to preserve the current record constructor:

```csharp
public sealed record Cp6DaprDeliveryValidationResult(
    bool IsValid,
    Cp6DaprContractFailure Failure,
    CloudEvent? CloudEvent)
{
    public const string ErrorCode = "CP6_DAPR_MESSAGE_INVALID";
    public ActivityContext? ParentContext { get; init; }
}
```

- [ ] **Step 4: Run full P04/P05 unit regression**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~CloudEventContractTests|FullyQualifiedName~SchemaCompatibilityTests|FullyQualifiedName~DaprKafkaContractTests|FullyQualifiedName~TraceContextContractTests"
```

Expected: all old and new tests pass; invalid telemetry alone does not invalidate an otherwise valid event.

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.Messaging/Cp6CloudEventAttributes.cs src/CP6.Platform.Messaging/Cp6CloudEventCodec.cs src/CP6.Platform.Messaging/Cp6TraceContextCodec.cs src/CP6.Platform.Messaging/Cp6DaprDeliveryValidator.cs tests/CP6.Platform.UnitTests/CloudEventContractTests.cs tests/CP6.Platform.UnitTests/DaprKafkaContractTests.cs tests/CP6.Platform.UnitTests/TraceContextContractTests.cs
git commit -m "feat(platform): propagate CloudEvent trace context"
```

## Task 8: Instrument Dapr/Kafka operations with safe activities and metrics

**Files:**

- Create: `src/CP6.Platform.Messaging/Cp6MessagingTelemetry.cs`
- Modify: `src/CP6.Platform.Messaging/Cp6DaprEventPublisher.cs`
- Modify: `src/CP6.Platform.Messaging/Cp6DaprServiceInvoker.cs`
- Modify: `src/CP6.Platform.Messaging/Cp6DaprDeliveryValidator.cs`
- Modify: `tests/CP6.Platform.UnitTests/DaprKafkaContractTests.cs`
- Create: `tests/CP6.Platform.UnitTests/MessagingTelemetryTests.cs`
- Modify: `tests/CP6.Platform.DaprKafkaFixture/Program.cs`

- [ ] **Step 1: Write activity/metric and side-effect-order tests**

Listen to `CP6.Platform.Messaging`, then prove publish/invoke/consume names, parent context, success/rejected/failure outcomes, stable error codes, transport value `dapr`, and forbidden-tag absence. Assert P04 schema/region and P05 topic/partition checks still occur before transport/handler side effects.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~MessagingTelemetryTests|FullyQualifiedName~DaprKafkaContractTests"
```

- [ ] **Step 3: Add one internal telemetry facade**

`Cp6MessagingTelemetry` owns a single `ActivitySource`, `Meter`, counters for published/consumed/rejected, and duration histograms. It accepts only stable operation/outcome/error/disposition/transport values and records no IDs, topic, body, exception message, host, tenant, or aggregate.

Start producer/client activities only after input/profile validation and before transport I/O. Consumer activities use the extracted parent when valid and an unparented root when invalid/missing. Preserve cancellation and every existing exception/result type.

- [ ] **Step 4: Run unit and real Dapr/Kafka regression**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~MessagingTelemetryTests|FullyQualifiedName~DaprKafkaContractTests"
pwsh eng/run-p05-integration.ps1
```

Expected: unit tests and real broker fixture pass with unchanged topic/key semantics.

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.Messaging/Cp6MessagingTelemetry.cs src/CP6.Platform.Messaging/Cp6DaprEventPublisher.cs src/CP6.Platform.Messaging/Cp6DaprServiceInvoker.cs src/CP6.Platform.Messaging/Cp6DaprDeliveryValidator.cs tests/CP6.Platform.UnitTests/DaprKafkaContractTests.cs tests/CP6.Platform.UnitTests/MessagingTelemetryTests.cs tests/CP6.Platform.DaprKafkaFixture/Program.cs
git commit -m "feat(platform): instrument messaging operations"
```

## Task 9: Instrument Outbox/Inbox without changing transactional semantics

**Files:**

- Create: `src/CP6.Platform.EntityFramework/Cp6EntityFrameworkTelemetry.cs`
- Modify: `src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs`
- Modify: `src/CP6.Platform.EntityFramework/Cp6OutboxDispatcher.cs`
- Modify: `src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs`
- Modify: `src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs`
- Modify: `tests/CP6.Platform.UnitTests/TransactionalMessagingContractTests.cs`
- Create: `tests/CP6.Platform.UnitTests/TransactionalMessagingTelemetryTests.cs`
- Modify: `tests/CP6.Platform.SqlServerFixture/Program.cs`

- [ ] **Step 1: Write instrumentation and transaction-regression tests**

Assert activity names and dispositions for enqueue/claim/publish/retry/dead-letter and validate/duplicate/conflict/applied/out-of-order/retry/dead-letter. Assert counters/histograms use only operation/outcome/error/disposition; oldest-available age and attempt counts are measurements, not labels. Keep rollback, lease, duplicate, conflict, ordering, replay, retention, and DLQ assertions from P06.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TransactionalMessagingTelemetryTests|FullyQualifiedName~TransactionalMessagingContractTests"
```

- [ ] **Step 3: Implement observer-only telemetry**

Use one internal facade owning source/meter/instruments. Wrap existing calls with `using var activity` and `try/finally` duration recording. Never call `SaveChanges`, mutate an entity, acquire a lease, or create a context from telemetry code. Record disposition only after the existing result is known. Preserve the current catch order and cancellation behavior.

- [ ] **Step 4: Run unit and real SQL Server regression**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~TransactionalMessagingTelemetryTests|FullyQualifiedName~TransactionalMessagingContractTests"
pwsh eng/run-p06-sql-integration.ps1
```

Expected: all P06 semantic assertions and new telemetry assertions pass.

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.EntityFramework/Cp6EntityFrameworkTelemetry.cs src/CP6.Platform.EntityFramework/Cp6OutboxStore.cs src/CP6.Platform.EntityFramework/Cp6OutboxDispatcher.cs src/CP6.Platform.EntityFramework/Cp6InboxProcessor.cs src/CP6.Platform.EntityFramework/Cp6MessageRetentionService.cs tests/CP6.Platform.UnitTests/TransactionalMessagingContractTests.cs tests/CP6.Platform.UnitTests/TransactionalMessagingTelemetryTests.cs tests/CP6.Platform.SqlServerFixture/Program.cs
git commit -m "feat(platform): instrument transactional messaging"
```

## Task 10: Build repository-only telemetry recorder and fault injector

**Files:**

- Modify: `src/CP6.Platform.Testing/CP6.Platform.Testing.csproj`
- Create: `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs`
- Create: `src/CP6.Platform.Testing/Cp6RecordedMetric.cs`
- Create: `src/CP6.Platform.Testing/Cp6HttpFaultScript.cs`
- Create: `src/CP6.Platform.Testing/Cp6HttpFaultHandler.cs`
- Create: `src/CP6.Platform.Testing/Cp6FaultInjectionServiceCollectionExtensions.cs`
- Modify: `tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj`
- Create: `tests/CP6.Platform.UnitTests/TestingUtilityTests.cs`

- [ ] **Step 1: Write utility tests**

Cover concurrent activity/metric capture, deterministic ordering, trace topology assertion, forbidden tag/value scan, scripted status/exception/delay/success outcomes, exact attempt count, caller cancellation, script exhaustion, and environment gate.

```csharp
[Theory]
[InlineData("Production")]
[InlineData("Development")]
public void FaultInjection_RejectsNonTestEnvironment(string environment)
{
    var services = new ServiceCollection();
    var host = new HostEnvironmentStub(environment);

    Assert.Throws<InvalidOperationException>(
        () => services.AddCp6HttpFaultInjection(host, new Cp6HttpFaultScript()));
}
```

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter FullyQualifiedName~TestingUtilityTests
```

- [ ] **Step 3: Implement thread-safe test support**

Use BCL `ActivityListener` and `MeterListener`; clone captured tags to immutable dictionaries; order snapshots by sequence assigned with `Interlocked.Increment`; implement `IDisposable` and stop listeners. The fault script is an immutable queue of explicit outcomes. Registration accepts only environment names exactly `Test` or `CI` and throws before adding services otherwise.

Set `CP6.Platform.Testing` to `IsPackable=false`; it remains solution-local and is never referenced by production projects.

- [ ] **Step 4: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release --filter FullyQualifiedName~TestingUtilityTests
```

- [ ] **Step 5: Commit**

```powershell
git add src/CP6.Platform.Testing/CP6.Platform.Testing.csproj src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs src/CP6.Platform.Testing/Cp6RecordedMetric.cs src/CP6.Platform.Testing/Cp6HttpFaultScript.cs src/CP6.Platform.Testing/Cp6HttpFaultHandler.cs src/CP6.Platform.Testing/Cp6FaultInjectionServiceCollectionExtensions.cs tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj tests/CP6.Platform.UnitTests/TestingUtilityTests.cs
git commit -m "test(platform): add deterministic observability fixtures"
```

## Task 11: Prove two-host trace topology and fault behavior end to end

**Files:**

- Modify: `tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj`
- Create: `tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs`
- Create: `tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs`
- Modify: `eng/verify.ps1`

- [ ] **Step 1: Add Testing reference and write failing two-host E2E**

Start Service B on an ephemeral loopback Kestrel port, then Service A on a different port. A must use a named HttpClient with P08 observability, outbound correlation, and resilience; B returns a stable JSON success. Capture Activity/Metric data with `Cp6TelemetryRecorder`.

Assert one W3C trace contains A server → A client → B server, all three Span IDs differ, resource service names/versions are correct, correlation is propagated independently from Trace ID, baggage is absent, and every CP6 tag is allowlisted. Add negative cases for malformed/duplicate/overlong trace headers, spoofed identity tags, B unavailable, caller cancellation, and a throwing `BaseExporter<Activity>` behind a bounded batch processor that cannot change the HTTP result.

Add deterministic fault cases: idempotent read fails twice then succeeds with exactly three attempts; non-idempotent is attempted once; missing idempotency key reaches the transport zero times; circuit open and recovery follow the scripted sequence.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~ObservabilityEndToEndTests
```

- [ ] **Step 3: Complete only the minimal wiring needed by the E2E**

Fix composition defects discovered by the E2E in the owning production file. Do not add a collector/exporter, environment address, dashboard, CRM route, fallback success, or test-only branch to production code.

- [ ] **Step 4: Update E2E verification without dropping P07**

Change `eng/verify.ps1` to run both suites:

```powershell
'--filter', 'FullyQualifiedName~GatewayContractTests|FullyQualifiedName~ObservabilityEndToEndTests'
```

Rename the check to `PlatformE2E`. Update package version strings to `0.8.0-alpha.1`, P07 error text to the approved P08 package set, Performance to `NotApplicable` because S01 freezes evidence contracts but no production threshold, and Migration to `NotApplicable` because S01 changes no database schema.

- [ ] **Step 5: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter FullyQualifiedName~ObservabilityEndToEndTests
pwsh eng/verify.ps1 -Gate E2E
```

- [ ] **Step 6: Commit**

```powershell
git add tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs eng/verify.ps1
git commit -m "test(platform): prove P08 cross-service behavior"
```

## Task 12: Harden contract/package gates and release tooling

**Files:**

- Modify: `eng/verify.ps1`
- Modify: `eng/pack-release.ps1`
- Modify: `eng/test-verify-failure.ps1`
- Modify: `.github/workflows/platform-validation.yml`
- Test: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Write failing package-content assertions**

Contract gate must require exactly five production `.nupkg` and `.snupkg` files, no `CP6.Platform.Testing` package, SLO assets only in Contracts, P04 bundle/event assets only in Messaging, no machine paths, and reproducible entry-level SHA-256 manifests across two packs.

Add architecture assertions that no production package includes test namespaces/assets and no code contains an OTLP endpoint, collector address, Grafana/Tempo/Prometheus dependency, production secret, or deployment route.

- [ ] **Step 2: Run and confirm RED**

```powershell
pwsh eng/verify.ps1 -Gate Contract
```

Expected: failure until the P08 asset/package rules replace the P07-only assertions.

- [ ] **Step 3: Update scripts and CI**

Set release default/version to `0.8.0-alpha.1`, keep the exact five-package array, validate non-empty `lib/net8.0/<id>.dll`, generate package hashes, and preserve the output-within-`artifacts` guard. Extend failure-contract self-tests for the new E2E/contract paths.

GitHub Actions must continue Windows and Linux format/build/unit/integration/E2E/contract/security and the real Dapr/Kafka and SQL Server jobs. Rename P07 labels to P08 where the ownership changed; do not weaken, skip, or delete any P04–P07 regression job.

- [ ] **Step 4: Run gates**

```powershell
pwsh eng/verify.ps1 -Gate Contract
pwsh eng/verify.ps1 -Gate Security
pwsh eng/test-verify-failure.ps1
```

Expected: all exit 0; vulnerability output contains no vulnerable package finding.

- [ ] **Step 5: Commit**

```powershell
git add eng/verify.ps1 eng/pack-release.ps1 eng/test-verify-failure.ps1 .github/workflows/platform-validation.yml tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git commit -m "build(platform): enforce P08 package evidence"
```

## Task 13: Document consumer contract, runbooks, and S01 publication readiness

**Files:**

- Create: `docs/P08-OBSERVABILITY-RESILIENCE.md`
- Create: `docs/P08-PUBLICATION.md`
- Create: `docs/runbooks/P08-TRACE-EXPORTER.md`
- Create: `docs/runbooks/P08-HEALTH-READINESS.md`
- Create: `docs/runbooks/P08-HTTP-RESILIENCE.md`
- Create: `docs/runbooks/P08-MESSAGING-BACKLOG.md`
- Create: `docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md`
- Modify: `README.md`
- Modify: `TESTING.md`
- Modify: `CHANGELOG.md`
- Test: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Add failing documentation completeness test**

Assert all seven P08 documents exist, mention S00–S06 status accurately, contain the stable endpoint/operation/error/schema IDs, state exporter/collector/CRM/runtime exclusions, and contain none of `TODO`, `TBD`, `FIXME`, production addresses, secrets, real owners, alert channels, or deploy commands.

- [ ] **Step 2: Run and confirm RED**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P08_Documentation_IsCompleteAndSafe
```

- [ ] **Step 3: Write the primary guide**

Document installation, exact public APIs, host-owned exporter rule, safe tags, health registration examples, release modes, retry decision table, CloudEvent tracing, SLO schema use, testing examples, and non-goals. Examples use synthetic names/digests and `productionSloClaimed=false`.

- [ ] **Step 4: Write all five runbooks with a common structure**

Each runbook must contain: symptoms, impact, stable dashboard/query ID, safe diagnosis, containment, recovery, validation, escalation, and evidence retention. Use IDs `CP6-P08-TRACE-001`, `CP6-P08-HEALTH-001`, `CP6-P08-RESILIENCE-001`, `CP6-P08-MESSAGING-001`, and `CP6-P08-RELEASE-001`. No production URL, credential, owner, channel, or deployment command.

- [ ] **Step 5: Record accurate S01 status**

`docs/P08-PUBLICATION.md` must say implementation candidate only until Platform PR/main CI completes; publication S02, CRM consumption S03, locator/public memory/final freeze S04–S06 are pending. Do not claim package publication or P08 frozen status.

- [ ] **Step 6: Run and confirm GREEN**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P08_Documentation_IsCompleteAndSafe
```

- [ ] **Step 7: Commit**

```powershell
git add docs/P08-OBSERVABILITY-RESILIENCE.md docs/P08-PUBLICATION.md docs/runbooks/P08-TRACE-EXPORTER.md docs/runbooks/P08-HEALTH-READINESS.md docs/runbooks/P08-HTTP-RESILIENCE.md docs/runbooks/P08-MESSAGING-BACKLOG.md docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md README.md TESTING.md CHANGELOG.md tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git commit -m "docs(platform): publish P08 operating contract"
```

## Task 14: Full verification, diff review, PR, and main evidence

**Files:**

- Modify only if evidence exposes a defect in a file already owned by Tasks 1–13.
- Evidence (generated, do not commit unless repository convention explicitly requires it): `artifacts/verify/**`

- [ ] **Step 1: Run local full gates**

```powershell
pwsh eng/verify.ps1 -Gate Format
pwsh eng/verify.ps1 -Gate Build
pwsh eng/verify.ps1 -Gate Unit
pwsh eng/verify.ps1 -Gate Integration
pwsh eng/verify.ps1 -Gate E2E
pwsh eng/verify.ps1 -Gate Contract
pwsh eng/verify.ps1 -Gate Security
pwsh eng/verify.ps1 -Gate Performance
pwsh eng/verify.ps1 -Gate Migration
pwsh eng/run-p05-integration.ps1
pwsh eng/run-p06-sql-integration.ps1
```

Expected: Format/Build/Unit/Integration/E2E/Contract/Security and both real profiles pass; Performance/Migration produce explicit `NotApplicable` evidence with P08 reasons.

- [ ] **Step 2: Self-review the complete branch**

```powershell
git status --short
git diff --check origin/main...HEAD
git diff --stat origin/main...HEAD
git diff --name-status origin/main...HEAD
git log --oneline --decorate origin/main..HEAD
git grep -n -E "TODO|TBD|FIXME|BEGIN (RSA|OPENSSH|EC) PRIVATE KEY|password[[:space:]]*=|connectionstring" -- . ":(exclude)artifacts" ":(exclude)docs/superpowers"
```

Review every changed file. Confirm no machine-specific path in production/docs, no real endpoint/secret, no package/source drift, no testing dependency in production, no business semantic changes hidden in instrumentation, and no unrelated file.

- [ ] **Step 3: Request code review and address findings with tests first**

Use `superpowers:requesting-code-review`. Any behavior change starts with a reproducing test. Re-run affected focused tests plus all gates after fixes. Commit fixes explicitly; do not amend already-reviewed commits unless the branch has not been shared.

- [ ] **Step 4: Push branch and create PR**

```powershell
git push -u origin codex/p08-s01-observability-resilience
gh pr create --base main --head codex/p08-s01-observability-resilience --title "P08-S01: observability, health, resilience, and SLO evidence" --body-file docs/P08-PUBLICATION.md
```

PR body must list scope/non-goals, local gate evidence, exact dependency/package versions, cross-service trace/fault results, P04–P07 regression results, and pending S02–S06.

- [ ] **Step 5: Wait for all remote checks and merge only when green**

Required evidence includes Windows/Linux validation plus real Dapr/Kafka and SQL Server. Do not merge a skipped, cancelled, stale, or red required job. Merge with normal repository policy; do not force-push or rewrite shared history.

- [ ] **Step 6: Verify Platform `main` after merge**

```powershell
git -C D:\CP6\CP6.Platform fetch origin
git -C D:\CP6\CP6.Platform rev-parse origin/main
git -C D:\CP6\CP6.Platform branch --contains origin/main
gh run list --repo GTX537/CP6.Platform --branch main --limit 5
```

Record the producer PR number, merge SHA, main workflow run ID/URL and all job conclusions in `docs/P08-PUBLICATION.md` on a separate evidence branch/PR if repository history requires post-merge evidence. Only then declare **P08-S01 complete** and begin the separate S02 plan. Do not call all of P08 complete.

## Plan self-review checklist

- [x] Every S00 approved requirement maps to at least one task and automated test.
- [x] Public types, package owners, source/meter names, endpoint paths, schema ID, operation kinds, failure categories, and runbook IDs are exact.
- [x] P04 seven required CloudEvent extensions remain required; trace fields are optional diagnostics.
- [x] P05 topic/key and P06 transaction/lease/idempotency/DLQ ordering remain unchanged and have real regression coverage.
- [x] Retry is possible only for explicit idempotent read/write rules; unknown/write-without-key fail closed.
- [x] Health output is safe, dependency ownership stays with consumers, and no exporter/backend/environment is provisioned.
- [x] Testing stays repository-only and cannot register fault injection outside Test/CI.
- [x] SLO `Pass` is impossible for NonCandidate, incomplete windows, invalid/missing digests, or partial evidence.
- [x] Five immutable package identities are preserved; `CP6.Platform.Testing` is excluded.
- [x] S01 does not claim S02 publication, S03 CRM consumption, S04 locator, S05 public memory, S06 final evidence, production SLO, or deployment.
- [x] No step uses broad staging, destructive cleanup, force-push, shared-history rewrite, remote-branch deletion, or production deployment.
