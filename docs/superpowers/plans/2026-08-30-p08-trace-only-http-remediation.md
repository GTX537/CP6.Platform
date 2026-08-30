# P08 Trace-Only HTTP Remediation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the published `0.8.0-alpha.1` HTTP baggage leak with a real two-host regression, ship an immutable `0.8.0-alpha.2` replacement, preserve exact publication evidence, and unblock CRM S03 without a consumer workaround.

**Architecture:** `AddCp6Observability` must align both propagation layers used by a real ASP.NET Core-to-HttpClient call: OpenTelemetry continues to use `TraceContextPropagator`, while `DistributedContextPropagator.Current` uses a CP6-owned trace-only wrapper around the BCL default propagator. The wrapper delegates W3C trace extraction/injection, filters all non-trace fields, and extracts no baggage. A two-Kestrel-host test observes actual downstream request headers, not only Activity state. The already-published alpha.1 artifacts remain immutable evidence but are disqualified as the CRM consumer candidate; alpha.2 is built, validated, published, and recorded from exact Platform main.

**Tech Stack:** .NET 8, ASP.NET Core Kestrel, `System.Diagnostics.DistributedContextPropagator`, OpenTelemetry 1.18.0, xUnit, PowerShell, GitHub Actions, GitHub Packages.

---

## Root cause and non-negotiable boundaries

- `Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator())` controls OpenTelemetry propagation but does not replace the `SocketsHttpHandler` BCL propagator that injects outbound HTTP headers.
- The BCL default propagator injects W3C trace fields and baggage. The existing producer tests asserted empty recorded Activity baggage but never inspected Service B's received headers.
- Disabling `SocketsHttpHandler.ActivityHeadersPropagator` removes baggage and also breaks the A-client-to-B-server W3C parent link. That is not an acceptable workaround.
- The fix belongs in Platform production composition. CRM must not carry a custom propagator, reference `CP6.Platform.Testing`, or weaken its downstream no-baggage assertion.
- `0.8.0-alpha.1` is immutable and must never be overwritten or pushed with duplicate skipping. The replacement version is exactly `0.8.0-alpha.2`.

## File map

| File | Responsibility |
| --- | --- |
| `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs` | Sample both `ActivityContext` and raw parent-ID activity creation so the producer fixture matches an external consumer listener |
| `tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs` | Return downstream trace/baggage header observations from the real Service B request |
| `tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs` | Prove one W3C chain reaches Service B while baggage does not |
| `tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs` | Prove both OTel and BCL global propagators advertise only `traceparent`/`tracestate` |
| `src/CP6.Platform.AspNetCore/Cp6TraceContextDistributedPropagator.cs` | Adapt the BCL default propagator to trace-only inject/extract behavior |
| `src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs` | Install the CP6 BCL propagator together with the existing OTel propagator |
| `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs` | Pin alpha.2 release-source and remediation-status invariants |
| `Directory.Build.props` | Set default package suffix to `alpha.2` |
| `eng/verify.ps1` | Verify and reproduce exact alpha.2 packages |
| `eng/pack-release.ps1` | Default release pack to alpha.2 |
| `.github/workflows/publish-alpha.yml` | Publish alpha.2 only from an exact current main commit |
| `docs/P08-OBSERVABILITY-RESILIENCE.md` | Document trace-only HTTP behavior and alpha.2 consumer target |
| `docs/P08-PUBLICATION.md` | Preserve alpha.1 evidence, mark it superseded, then record alpha.2 evidence |
| `docs/runbooks/P08-*.md` | Keep the P08 stage status truthful during remediation and after publication |
| `README.md`, `CHANGELOG.md` | Record the security correction and immutable replacement boundary |

### Task 1: Preserve the consumer-discovered producer failure as a real two-host RED test

**Files:**
- Modify: `src/CP6.Platform.Testing/Cp6TelemetryRecorder.cs`
- Modify: `tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs`
- Modify: `tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs`
- Modify: `tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs`

- [ ] **Step 1: Make the repository recorder sample raw parent-ID activities like a consumer listener**

Add the callback that the CRM-owned recorder already proved is required for the raw-parent request path:

```csharp
SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
    ActivitySamplingResult.AllDataAndRecorded,
```

Keep the existing `Sample` callback. This is repository-only test support and does not enter any runtime package.

- [ ] **Step 2: Expose only boolean downstream header observations**

Change Service B's successful response to include whether it received `traceparent` and `baggage`, without echoing either value:

```csharp
return Results.Json(new ProxyResponse(
    true,
    context.TraceIdentifier,
    string.Empty,
    context.Request.Headers.ContainsKey("traceparent"),
    context.Request.Headers.ContainsKey("baggage")));
```

Extend the fixture response contract with safe optional fields so existing failure responses remain unchanged:

```csharp
internal sealed record ProxyResponse(
    bool Success,
    string CorrelationId,
    string ErrorCode,
    bool HasTraceParentHeader = false,
    bool HasBaggageHeader = false);
```

- [ ] **Step 3: Strengthen the two-host test before production changes**

Send a secret sentinel as incoming baggage and assert the actual Service B request received W3C trace context but no baggage:

```csharp
var response = await fixture.SendRawAsync(
    HttpMethod.Get,
    "/proxy/read",
    ("X-Correlation-Id", "business-correlation"),
    ("baggage", "unsafe=secret-baggage"));
var body = JsonSerializer.Deserialize<ProxyResponse>(response.Body, JsonOptions());

Assert.NotNull(body);
Assert.True(body.HasTraceParentHeader);
Assert.False(body.HasBaggageHeader);
```

Keep the existing server/client/server parent assertions and empty recorded baggage assertions unchanged.

- [ ] **Step 4: Pin both global propagation surfaces in the registration test**

After calling `AddCp6Observability`, retain the existing OpenTelemetry assertion and add:

```csharp
Assert.Equal(
    new[] { "traceparent", "tracestate" },
    DistributedContextPropagator.Current.Fields.OrderBy(field => field, StringComparer.Ordinal));
```

- [ ] **Step 5: Run RED and retain the exact failure**

Run:

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --filter "FullyQualifiedName~TwoHosts_ProduceOneW3cTraceWithIndependentCorrelationAndSafeResources|FullyQualifiedName~AddCp6Observability_UsesTraceContextWithoutBaggage" --logger "console;verbosity=normal"
```

Expected: the downstream header assertion reports `HasBaggageHeader == true` and/or the BCL fields include baggage. The W3C trace assertion must still be true. A compile error or unrelated failure is not acceptable RED evidence.

### Task 2: Install one trace-only BCL propagator and make the regression GREEN

**Files:**
- Create: `src/CP6.Platform.AspNetCore/Cp6TraceContextDistributedPropagator.cs`
- Modify: `src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs`
- Test: `tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs`

- [ ] **Step 1: Add the internal trace-only BCL adapter**

Create:

```csharp
using System.Diagnostics;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6TraceContextDistributedPropagator : DistributedContextPropagator
{
    private static readonly DistributedContextPropagator Inner = CreateDefaultPropagator();
    private static readonly string[] TraceFields = ["traceparent", "tracestate"];

    internal static Cp6TraceContextDistributedPropagator Instance { get; } = new();

    public override IReadOnlyCollection<string> Fields => TraceFields;

    public override void Inject(
        Activity? activity,
        object? carrier,
        PropagatorSetterCallback? setter)
    {
        if (setter is null)
        {
            return;
        }

        Inner.Inject(activity, carrier, (target, fieldName, fieldValue) =>
        {
            if (TraceFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
            {
                setter(target, fieldName, fieldValue);
            }
        });
    }

    public override void ExtractTraceIdAndState(
        object? carrier,
        PropagatorGetterCallback? getter,
        out string? traceParent,
        out string? traceState) =>
        Inner.ExtractTraceIdAndState(carrier, getter, out traceParent, out traceState);

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
        object? carrier,
        PropagatorGetterCallback? getter) => null;
}
```

- [ ] **Step 2: Align BCL and OpenTelemetry registration**

Immediately before the existing OpenTelemetry propagator call, set:

```csharp
DistributedContextPropagator.Current = Cp6TraceContextDistributedPropagator.Instance;
Sdk.SetDefaultTextMapPropagator(new TraceContextPropagator());
```

Do not add exporter configuration, per-client transport overrides, baggage parsing, or public API surface.

- [ ] **Step 3: Run focused GREEN tests**

Run the same focused command from Task 1.

Expected: 2/2 pass. The downstream response reports `HasTraceParentHeader=true` and `HasBaggageHeader=false`, while the recorded chain remains A-server → A-client → B-server.

- [ ] **Step 4: Run every ASP.NET Core test**

```powershell
dotnet test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release --logger "console;verbosity=minimal"
```

Expected: all tests pass with no warning or error output.

- [ ] **Step 5: Review and commit the production correction**

```powershell
git diff --check
git diff -- src/CP6.Platform.AspNetCore tests/CP6.Platform.AspNetCoreTests
git add -- src/CP6.Platform.AspNetCore/Cp6TraceContextDistributedPropagator.cs src/CP6.Platform.AspNetCore/Cp6ObservabilityServiceCollectionExtensions.cs tests/CP6.Platform.AspNetCoreTests/TwoServiceObservabilityFixture.cs tests/CP6.Platform.AspNetCoreTests/ObservabilityEndToEndTests.cs tests/CP6.Platform.AspNetCoreTests/ObservabilityRegistrationTests.cs
git diff --cached --check
git commit -m "fix(observability): block outbound HTTP baggage"
```

### Task 3: Move release source to immutable alpha.2 and make release invariants testable

**Files:**
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`
- Modify: `Directory.Build.props`
- Modify: `eng/verify.ps1`
- Modify: `eng/pack-release.ps1`
- Modify: `.github/workflows/publish-alpha.yml`

- [ ] **Step 1: Write release-source assertions first**

Extend `P08_DependencyAndPackageBoundary_IsExact`:

```csharp
Assert.Equal("0.8.0", buildProperties.Descendants("VersionPrefix").Single().Value);
Assert.Equal("alpha.2", buildProperties.Descendants("VersionSuffix").Single().Value);
```

Extend `P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded`:

```csharp
Assert.Contains("$packageVersion = '0.8.0-alpha.2'", verify, StringComparison.Ordinal);
Assert.Contains("[string]$PackageVersion = '0.8.0-alpha.2'", pack, StringComparison.Ordinal);
```

Extend `P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence`:

```csharp
Assert.Contains(
    "./eng/pack-release.ps1 -OutputDirectory artifacts/release -PackageVersion 0.8.0-alpha.2",
    workflow,
    StringComparison.Ordinal);
```

- [ ] **Step 2: Run the architecture test and retain RED**

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter "FullyQualifiedName~P08_DependencyAndPackageBoundary_IsExact|FullyQualifiedName~P08_PackageEvidenceAndProductionSafetyGuards_AreEncoded|FullyQualifiedName~P08_PublicationWorkflow_AlwaysPreservesCompleteEvidence"
```

Expected: failure because current release source is alpha.1.

- [ ] **Step 3: Change exactly four release-source values**

- `Directory.Build.props`: `<VersionSuffix>alpha.2</VersionSuffix>`
- `eng/verify.ps1`: `$packageVersion = '0.8.0-alpha.2'`
- `eng/pack-release.ps1`: `[string]$PackageVersion = '0.8.0-alpha.2'`
- `.github/workflows/publish-alpha.yml`: `-PackageVersion 0.8.0-alpha.2`

Do not change repository `VERSION` (`0.8.0.0`) and do not add `--skip-duplicate`.

- [ ] **Step 4: Run the focused architecture tests GREEN**

Run the same command from Step 2.

Expected: all three selected tests pass.

- [ ] **Step 5: Pack alpha.2 locally and prove the exact ten-file set**

```powershell
pwsh ./eng/pack-release.ps1 -OutputDirectory artifacts/release-alpha2
$packages = @(Get-ChildItem artifacts/release-alpha2 -File | Where-Object { $_.Name -like '*.nupkg' -or $_.Name -like '*.snupkg' } | Sort-Object Name)
if ($packages.Count -ne 10) { throw "Expected ten alpha.2 package files, got $($packages.Count)." }
if ($packages.Name | Where-Object { $_ -notmatch '^CP6\.Platform\.(Abstractions|AspNetCore|Contracts|EntityFramework|Messaging)\.0\.8\.0-alpha\.2\.(nupkg|snupkg)$' }) {
    throw 'Unexpected alpha.2 package filename.'
}
```

- [ ] **Step 6: Commit the alpha.2 release source**

```powershell
git add -- Directory.Build.props eng/verify.ps1 eng/pack-release.ps1 .github/workflows/publish-alpha.yml tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "build: advance P08 remediation candidate to alpha.2"
```

### Task 4: Make remediation status and consumer guidance truthful

**Files:**
- Modify: `docs/P08-OBSERVABILITY-RESILIENCE.md`
- Modify: `docs/P08-PUBLICATION.md`
- Modify: `docs/runbooks/P08-TRACE-EXPORTER.md`
- Modify: `docs/runbooks/P08-HEALTH-READINESS.md`
- Modify: `docs/runbooks/P08-HTTP-RESILIENCE.md`
- Modify: `docs/runbooks/P08-MESSAGING-BACKLOG.md`
- Modify: `docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Change the documentation status assertion first**

While alpha.2 has not yet been published, require this exact line in all seven P08 documents:

```text
P08 status: S00-S01 complete; S02 remediation pending; S03-S06 pending.
```

Run `P08_Documentation_IsCompleteAndSafe` and retain the expected RED because documents still claim S02 complete.

- [ ] **Step 2: Update current guidance without erasing alpha.1 evidence**

In `docs/P08-PUBLICATION.md`, add a leading remediation decision that states:

```text
The immutable 0.8.0-alpha.1 packages remain historical publication evidence but are disqualified as the CRM consumer candidate: the real downstream request still received baggage because only the OpenTelemetry propagator, not the BCL HttpClient propagator, was constrained. The forward-only replacement is 0.8.0-alpha.2. No alpha.1 artifact is overwritten or deleted.
```

Keep the complete alpha.1 run/artifact/hash ledger intact. In `docs/P08-OBSERVABILITY-RESILIENCE.md`, set the target package references to exact `0.8.0-alpha.2` and state that `AddCp6Observability` aligns both OpenTelemetry and BCL HTTP propagation to `traceparent`/`tracestate` only. Update README and CHANGELOG with the same bounded correction; do not claim CRM consumption or `Frozen / Consumable`.

- [ ] **Step 3: Update all seven status lines and verify GREEN**

Run:

```powershell
dotnet test tests/CP6.Platform.ArchitectureTests/CP6.Platform.ArchitectureTests.csproj --configuration Release --filter FullyQualifiedName~P08_Documentation_IsCompleteAndSafe
git grep -n -I -E "TODO|TBD|FIXME" -- docs/P08-PUBLICATION.md docs/P08-OBSERVABILITY-RESILIENCE.md docs/runbooks README.md CHANGELOG.md
```

Expected: documentation test passes and placeholder scan has no output.

- [ ] **Step 4: Commit remediation documentation**

```powershell
git add -- docs/P08-PUBLICATION.md docs/P08-OBSERVABILITY-RESILIENCE.md docs/runbooks/P08-TRACE-EXPORTER.md docs/runbooks/P08-HEALTH-READINESS.md docs/runbooks/P08-HTTP-RESILIENCE.md docs/runbooks/P08-MESSAGING-BACKLOG.md docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md README.md CHANGELOG.md tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs
git diff --cached --check
git commit -m "docs: record P08 trace propagation remediation"
```

### Task 5: Run complete local gates, land the repair PR, and verify exact main

**Files:** none beyond Tasks 1-4.

- [ ] **Step 1: Run all non-container release gates**

```powershell
pwsh ./eng/verify.ps1 -Gate Format -Profile release
pwsh ./eng/verify.ps1 -Gate Build -Profile release
pwsh ./eng/verify.ps1 -Gate Unit -Profile release
pwsh ./eng/verify.ps1 -Gate Integration -Profile release
pwsh ./eng/verify.ps1 -Gate E2E -Profile release
pwsh ./eng/verify.ps1 -Gate Contract -Profile release
pwsh ./eng/verify.ps1 -Gate Security -Profile release
pwsh ./eng/verify.ps1 -Gate Performance -Profile release
pwsh ./eng/verify.ps1 -Gate Migration -Profile release
pwsh ./eng/test-verify-failure.ps1
git diff --check
```

Expected: all applicable gates pass; Performance and Migration are explicitly `NotApplicable`; failure-contract self-test passes.

- [ ] **Step 2: Review the complete branch diff and sensitive-content boundary**

```powershell
git diff --stat main...HEAD
git diff main...HEAD
git grep -n -I -E "ghp_|github_pat_|ClearTextPassword|packageSourceCredentials" -- . ':!docs/superpowers/plans'
```

Expected: only P08 trace-only remediation, alpha.2 release source, tests, plan, and truthful docs are present; secret scan is empty.

- [ ] **Step 3: Push and open one repair PR**

```powershell
git push -u origin codex/p08-trace-only-http-propagation
gh pr create --repo GTX537/CP6.Platform --base main --head codex/p08-trace-only-http-propagation --title "fix(platform): enforce trace-only HTTP propagation" --body "Adds a real downstream-header regression, aligns BCL and OpenTelemetry propagation, advances the immutable P08 replacement to 0.8.0-alpha.2, and preserves alpha.1 as superseded evidence. S03-S06 remain pending."
```

- [ ] **Step 4: Require all four remote jobs and review the PR diff**

```powershell
gh pr checks --repo GTX537/CP6.Platform codex/p08-trace-only-http-propagation --watch
gh pr diff --repo GTX537/CP6.Platform codex/p08-trace-only-http-propagation
gh pr view --repo GTX537/CP6.Platform codex/p08-trace-only-http-propagation --json number,url,headRefOid,mergeStateStatus,statusCheckRollup
```

Expected: Ubuntu, Windows, real Dapr/Kafka, and real SQL Server jobs all succeed.

- [ ] **Step 5: Merge and require the exact post-merge main run**

```powershell
gh pr merge --repo GTX537/CP6.Platform codex/p08-trace-only-http-propagation --merge
git fetch origin main
$approvedMainSha = (git rev-parse origin/main).Trim()
$mainRun = gh run list --repo GTX537/CP6.Platform --workflow platform-validation.yml --branch main --event push --limit 20 --json databaseId,url,headSha,status,conclusion | ConvertFrom-Json | Where-Object headSha -eq $approvedMainSha | Select-Object -First 1
gh run watch --repo GTX537/CP6.Platform $mainRun.databaseId --exit-status
```

Expected: the run bound to `$approvedMainSha` succeeds with all four jobs. Do not publish from the PR head or from a drifting main.

### Task 6: Publish and independently verify exact alpha.2 artifacts

**Files:** downloaded evidence under a fresh temporary directory only.

- [ ] **Step 1: Confirm alpha.2 does not already exist**

Query all five GitHub Packages IDs with authenticated package-read access and reject any existing `0.8.0-alpha.2` version. If one exists, stop and audit its source; never invoke duplicate skipping.

- [ ] **Step 2: Dispatch from exact current main**

```powershell
$dispatchStartedAt = [DateTimeOffset]::UtcNow
gh workflow run publish-alpha.yml --repo GTX537/CP6.Platform --ref main -f expected_commit=$approvedMainSha
$publishRun = gh run list --repo GTX537/CP6.Platform --workflow publish-alpha.yml --branch main --event workflow_dispatch --limit 20 --json databaseId,url,headSha,createdAt,status,conclusion | ConvertFrom-Json | Where-Object { $_.headSha -eq $approvedMainSha -and [DateTimeOffset]$_.createdAt -ge $dispatchStartedAt.AddMinutes(-1) } | Sort-Object createdAt -Descending | Select-Object -First 1
gh run watch --repo GTX537/CP6.Platform $publishRun.databaseId --exit-status
```

- [ ] **Step 3: Reject terminal main drift**

Fetch `origin/main` and require it still equals `$approvedMainSha`. If upload may have started and main drifted, preserve the run and stop for a new version decision.

- [ ] **Step 4: Download and verify evidence**

Require run event `workflow_dispatch`, branch `main`, head `$approvedMainSha`, and conclusion `success`. Require exactly one artifact named `p08-alpha-$approvedMainSha`, capture its API ID/digest/size/timestamps, and download it to a fresh temporary directory. Require exactly these ten files:

```text
CP6.Platform.Abstractions.0.8.0-alpha.2.nupkg
CP6.Platform.Abstractions.0.8.0-alpha.2.snupkg
CP6.Platform.AspNetCore.0.8.0-alpha.2.nupkg
CP6.Platform.AspNetCore.0.8.0-alpha.2.snupkg
CP6.Platform.Contracts.0.8.0-alpha.2.nupkg
CP6.Platform.Contracts.0.8.0-alpha.2.snupkg
CP6.Platform.EntityFramework.0.8.0-alpha.2.nupkg
CP6.Platform.EntityFramework.0.8.0-alpha.2.snupkg
CP6.Platform.Messaging.0.8.0-alpha.2.nupkg
CP6.Platform.Messaging.0.8.0-alpha.2.snupkg
```

Independently recompute every SHA-256 and match `sha256.json`; require non-empty runtime assemblies, no Testing assets, correct Contracts/Messaging asset ownership, non-empty P05/P06 real evidence, and complete verify summaries.

### Task 7: Record alpha.2 evidence on a separate branch and re-close S02

**Files:**
- Modify: `docs/P08-PUBLICATION.md`
- Modify: `docs/P08-OBSERVABILITY-RESILIENCE.md`
- Modify: `docs/runbooks/P08-TRACE-EXPORTER.md`
- Modify: `docs/runbooks/P08-HEALTH-READINESS.md`
- Modify: `docs/runbooks/P08-HTTP-RESILIENCE.md`
- Modify: `docs/runbooks/P08-MESSAGING-BACKLOG.md`
- Modify: `docs/runbooks/P08-RELEASE-EVIDENCE-DRIFT.md`
- Modify: `CHANGELOG.md`
- Modify: `tests/CP6.Platform.ArchitectureTests/RepositoryArchitectureTests.cs`

- [ ] **Step 1: Create `codex/p08-alpha2-publication-evidence` from exact published main**

Use a separate clean worktree based on `$approvedMainSha`. Do not reuse the repair branch and do not delete either remote branch.

- [ ] **Step 2: Change the documentation assertion first and retain RED**

Require this exact line in all seven P08 documents:

```text
P08 status: S00-S02 complete; S03-S06 pending.
```

Run `P08_Documentation_IsCompleteAndSafe`; expect failure until all documents are reconciled.

- [ ] **Step 3: Record only exact captured evidence**

Append the repair PR head/merge, PR four-job run, exact publication-source main SHA/run, publish run/job, artifact API identity, exact ten filenames, five ordinary package hashes, manifest verification, package safety, and P05/P06 evidence. State that alpha.2 supersedes alpha.1 for consumption without deleting alpha.1 history. Do not claim CRM consumption or `Frozen / Consumable`.

- [ ] **Step 4: Run documentation and full release gates, then commit**

Run the documentation test, all non-container release gates, secret/placeholder scans, and `git diff --check`. Commit only the exact evidence/status files with:

```powershell
git commit -m "docs(platform): record P08 alpha.2 publication evidence"
```

- [ ] **Step 5: Open, validate, and merge the evidence PR**

Require all four remote jobs, review the complete diff against exact captured API/artifact facts, merge, and require the exact post-evidence main workflow to pass. Only then is S02 remediation complete again.

### Task 8: Resume CRM S03 on the verified alpha.2 ledger

Return to `docs/superpowers/plans/2026-08-30-p08-s03-crm-consumer.md` in the existing CRM worktree. Update the five exact package references, package hashes, Platform source/run/artifact facts, and locator assertions from alpha.1 to the verified alpha.2 evidence. Re-run the already-red `PlatformObservabilityConsumerTests` without a CRM propagator workaround; it must become 2/2 GREEN solely because the production Platform package is corrected. Then continue the remaining S03-S06 plan.

## Self-review result

- Spec coverage: real downstream header observation, W3C topology, no baggage, both propagation layers, immutable forward version, exact-main publication, artifact verification, status docs, and CRM resume are all assigned to explicit tasks.
- Placeholder scan: the plan contains no implementation placeholders; publication-only values are captured from bound GitHub API/run/artifact results before documentation is edited.
- Type consistency: `ProxyResponse.HasTraceParentHeader`/`HasBaggageHeader`, `Cp6TraceContextDistributedPropagator.Instance`, the exact alpha.2 version, branch names, and gate commands are consistent across tasks.
