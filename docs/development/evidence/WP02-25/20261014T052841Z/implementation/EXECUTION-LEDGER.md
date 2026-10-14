# Implementation execution ledger — WP02-25

Workspace: `D:/CP6.Platform-worktrees/wp02-25-20261013`.
Branch/base provided by coordinator: `codex/wp02-25-observability-20261013`,
`e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`.
All .NET commands below used `C:/Users/tt/.dotnet/dotnet.exe` (SDK 8.0.424).
No commit, push, Actions, deployment, database operation, package publish or
project/dependency-file edit was performed by the implementation agent.

## Interpretation and provenance limits

- Run-directory sequence is chronological; names such as `green` or `final`
  describe intention, never establish the actual outcome.
- Runs 01–17 did **not** immediately capture the numeric dotnet return code. The
  enclosing PowerShell command printed a log tail after dotnet, so the tool's
  wrapper exit code is not a dotnet exit code. Those numeric codes are
  **not captured**, not assumed zero or reconstructed as one. Outcomes below
  come only from compiler logs or actual TRX counters.
- Runs 18–22 persist `$LASTEXITCODE` immediately after each dotnet invocation in
  `*-exit-code.txt`. This is the actual captured dotnet exit code.
- Intermediate runs did not capture a before/after hash snapshot of all working
  source or binaries. They operated on the successive local implementation at
  that time. Do not claim they ran against the final candidate. Parent delivery
  gates separately pin final source, dependencies and binaries.
- The initial test-first API additions produced compiler failures because the
  API did not exist. They are **compile-red evidence**, not executed assertion
  failures. Executed assertion failures are specifically identified below.
- The generated assemblies' informational source SHA names the base commit;
  working files were changing. Source metadata is not proof of clean source,
  an independently built release or trusted package provenance.
- All secrets in the tests are synthetic markers. Kestrel addresses are isolated
  ephemeral loopback ports. No production endpoint, database or real Owner
  receipt was used. Original acceptance cases remain outside this evidence scope.

## Exact command templates

Set `R` to the run subdirectory in the table and `P`, `F`, `T` to the listed
project, filter and TRX filename. These are literal command substitutions for
documentation, not a new script or claimed execution.

```powershell
& C:/Users/tt/.dotnet/dotnet.exe test P --no-restore --filter F --logger 'trx;LogFileName=T' --results-directory docs/development/evidence/WP02-25/20261014T052841Z/implementation/R
```

Every test invocation redirected all output to the log in its own run directory.
Most used `console.log`; runs 20 and 21 used `aspnet.log` and `unit.log`.
TRX is `results.trx` except runs 20 and 21, which use `aspnet.trx`/`unit.trx`.

Project abbreviations:

- `Unit`: `tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj`
- `AspNet`: `tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj`

The only restore commands (run 02) were:

```powershell
& C:/Users/tt/.dotnet/dotnet.exe restore tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --source C:/Users/tt/.nuget/packages --ignore-failed-sources
& C:/Users/tt/.dotnet/dotnet.exe restore tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --source C:/Users/tt/.nuget/packages --ignore-failed-sources
```

They used the existing local package cache as the only package source. Output is
`02-restore/aspnet.log` and `02-restore/unit.log`.

## Chronology and actual outcomes

| Run directory | Project / exact filter | Actual outcome | Numeric dotnet exit |
|---|---|---|---|
| 01-contract-red | Unit / `FullyQualifiedName~ObservabilityV1` | NETSDK1004, missing local assets; no tests executed | Not captured |
| 02-restore | Restore commands above | Logs report successful restores | Not captured |
| 03-contract-red | Unit / `FullyQualifiedName~ObservabilityV1` | Compile-red: new namespace and binding missing | Not captured |
| 04-contract-green | Unit / `FullyQualifiedName~ObservabilityV1` | 4 passed, 0 failed | Not captured |
| 05-runtime-health-red | AspNet / `FullyQualifiedName~ObservabilityV1` | Compile-red: runtime/health API missing | Not captured |
| 06-runtime-health-green | AspNet / `FullyQualifiedName~ObservabilityV1` | 5 passed, 1 failed: immediate dependency restart recovery assertion | Not captured |
| 07-telemetry-red | AspNet / `FullyQualifiedName~ObservabilityV1` | Compile-red: telemetry API missing | Not captured |
| 08-telemetry-green | AspNet / `FullyQualifiedName~ObservabilityV1` | Compile failure: xUnit2031 analyzer rejected `Where` before `Assert.Single` | Not captured |
| 09-runtime-telemetry-green | AspNet / `FullyQualifiedName~ObservabilityV1` | 7 passed, 2 failed: missing automatic server traces, including multi-host case | Not captured |
| 10-telemetry-regression | AspNet / `FullyQualifiedName~ObservabilityV1` | 9 passed, 0 failed | Not captured |
| 11-telemetry-bounds-red | AspNet / `FullyQualifiedName~AdversarialTelemetryTests` | 5 passed, 1 failed: pre-aggregation measurement rate not enforced | Not captured |
| 12-negative-matrix | AspNet / `FullyQualifiedName~ObservabilityV1` | 37 passed, 1 failed: later route collision did not fail host startup | Not captured |
| 13-route-collision-green | AspNet / `FullyQualifiedName~HealthNegativeMatrixTests\|FullyQualifiedName~AdversarialTelemetryTests` | 13 passed, 0 failed | Not captured |
| 14-source-metadata-red | AspNet / `FullyQualifiedName~IdentityNegativeMatrixTests` | Compile-red: independently observed source revision API missing | Not captured |
| 15-source-metadata-green | AspNet / `FullyQualifiedName~IdentityNegativeMatrixTests` | 18 passed, 0 failed | Not captured |
| 16-capability-binding-red | AspNet / `FullyQualifiedName~HealthIdentityAndPermissionTests` | Compile-red: required-module capability constructor missing | Not captured |
| 17-capability-binding-green | AspNet / `FullyQualifiedName~HealthIdentityAndPermissionTests\|FullyQualifiedName~HealthBindingTests` | 5 passed, 0 failed | Not captured |
| 18-envelope-validation-red | Unit / `FullyQualifiedName~ObservabilityV1` | 8 passed, 1 failed: undefined envelope enum accepted | 1 |
| 19-envelope-green | Unit / `FullyQualifiedName~ObservabilityV1` | 9 passed, 0 failed | 0 |
| 20-clock-definitions-red | AspNet / `FullyQualifiedName~ObserverClockException` | 0 passed, 1 failed: clock exception escaped | 1 |
| 20-clock-definitions-red | Unit / `FullyQualifiedName~ExistingTechnicalMetrics` | 0 passed, 5 failed: technical metric definitions missing | 1 |
| 21-final-targeted | AspNet / `FullyQualifiedName~ObservabilityV1` | 43 passed, 0 failed | 0 |
| 21-final-targeted | Unit / `FullyQualifiedName~ObservabilityV1` | 14 passed, 0 failed | 0 |
| 22-batch-final-regression | AspNet / `FullyQualifiedName~AdversarialTelemetryTests\|FullyQualifiedName~TelemetryAdapterTests` | 10 passed, 0 failed | 0 |

In the Markdown table, `\|` escapes the display separator; the actual command
filters used the literal `|` character inside a single-quoted PowerShell string.

## Fixes and executed evidence

- The dependency restart fixture originally asserted the first request after
  restarting on the same port. It now allows up to 20 bounded 50-ms polling
  attempts for recovery; it still requires actual HTTP readiness recovery.
- The trace regression exposed the shared-Activity effect of SDK instrumentation
  filters in multiple providers. Request ownership is now assigned from the
  actual hosting middleware and filtered only by each safe processor.
- The measurement-rate assertion failed on the actual SDK-exported sum. A rate
  gate now runs before sanitized private-Meter aggregation.
- ASP.NET lifetime notifications swallowed the initial late-collision exception.
  Complete operational-route validation now runs in an `IStartupFilter` before
  the server listens.
- Source metadata is now read from actual loaded assembly metadata; wrong source
  pins and wrong declared host Git SHA have explicit negative coverage.
- New-effect routes must bind a relevant owned module. Authorized HTTP readiness
  is tested while identity ages to `Unproven`; read availability remains separate.
- Undefined envelope enums and observer clock exceptions have explicit failing
  then passing coverage.
- Final sink tests cover actual SDK traces/metrics/logs, inbound and outbound HTTP,
  traceparent without automatic baggage, events/links, high-cardinality values,
  unsafe log formatter/state/scopes, resource projection, series/rate/size bounds,
  queue overflow, batch/lifetime/retention bounds, blocked/throwing sink behavior,
  flush and repeated/multiple-host propagation drift/disposal.
- `RuntimeIdentityTests` and `TelemetryAdapterTests` write actual fixture digests,
  observation scope, Kestrel addresses/HTTP status and safe final sink records to
  xUnit output retained by TRX. These are local execution evidence only.

Run 22 added one integration test and changed only an internal safe-sink fault
injection hook to inspect actual batch size. The expected final new test inventory
is **44 AspNet tests and 14 Unit tests**. The implementation agent relinquished
.NET ownership after run 22. Required legacy/architecture/final-candidate gates
and complete task reviews belong to the coordinator; this ledger does not claim
they have run or passed.

## Consolidated spec-review corrections (runs 23–25)

The coordinator returned three substantive findings in one correction batch:
route-specific dependency/module isolation, absolute canonical string endings,
and validation of the observer's own timestamp. The implementation agent regained
sole .NET runner ownership for the following targeted checks. All these invocations
used the same executable, workspace and command template above and immediately
persisted the numeric dotnet return code.

| Run directory | Project / exact filter | Actual outcome | Numeric dotnet exit |
|---|---|---|---|
| 23-canonical-clock-red | Unit / `FullyQualifiedName~CanonicalBoundaryTests` | 3 passed, 1 failed: identifier with trailing LF accepted | 1 |
| 23-canonical-clock-red | AspNet / `FullyQualifiedName~InvalidObserverTimestamp\|FullyQualifiedName~FutureObserverTimestamp` | 1 passed, 3 failed: nonzero-offset/epoch/minimum observer times accepted | 1 |
| 24-route-binding-red | AspNet / `FullyQualifiedName~RouteCapabilityTests` | Compile-red: per-route check inventory and route observation API absent | 1 |
| 25-review-corrections-green | Unit / `FullyQualifiedName~ObservabilityV1` | 18 passed, 0 failed | 0 |
| 25-review-corrections-green | AspNet / `FullyQualifiedName~RouteCapabilityTests\|FullyQualifiedName~HealthBindingTests\|FullyQualifiedName~HealthNegativeMatrixTests\|FullyQualifiedName~HealthIdentityAndPermissionTests\|FullyQualifiedName~RuntimeIdentityTests\|FullyQualifiedName~IdentityNegativeMatrixTests` | 42 passed, 0 failed | 0 |

Runs 23 and 25 use `unit.log`, `aspnet.log`, `unit.trx`, `aspnet.trx` and corresponding
`unit-exit-code.txt`/`aspnet-exit-code.txt`. Run 24 uses `console.log`, `results.trx`
(not emitted because compilation failed) and `dotnet-exit-code.txt`.

Changes and evidence scope:

- Every capability route now requires a copied, nonempty, unique inventory of
  named readiness bindings. Missing/empty/unknown/duplicate/wrong-purpose mappings
  fail closed. Existing fixtures and consumer examples supply explicit mappings.
- One actual observer snapshot computes per-module pin verdicts alongside its
  conservative whole-inventory verdict. Common binding failure invalidates all
  module verdicts; a bad module B does not erase module A's valid local evidence.
- Readiness reports each route's dependency failure, module verdict and availability.
  A write-only dependency failure leaves an independently healthy read route
  available. The `ReadAvailable` and `NewEffectsAvailable` summaries mean at least
  one route is available; whole-health HTTP status remains conservative and fails
  on any required failure, including a hidden component or module mismatch.
- Real Kestrel tests verify read/write dependency isolation and two independently
  loaded module routes, including a common host-binding mismatch. They assert
  HTTP 503 and independent route availability in the same actual response.
- All newly introduced canonical regexes use absolute `\A`/`\z` boundaries.
  LF, CRLF, tab and NUL negatives cover identifiers, digests, module names, source
  pins and both telemetry/capability route templates. Legacy validators are untouched.
- The observer validates its original timestamp before loading modules; invalid
  timestamp/clock errors use the existing safe load-failure boundary. Evaluation
  independently validates observation UTC/epoch/future/freshness constraints.

No broader telemetry rerun was performed in this correction batch: its unchanged
runtime/exporter behavior retains its earlier evidence scope; the strict template
validator is covered by current contract tests. Parent final gates establish the
complete candidate. After this batch the expected new inventory is **52 AspNet
tests and 18 Unit tests**. Source remains uncommitted; no final gate or production
acceptance claim is made here.

## Consolidated quality-review corrections (runs 26–27)

The coordinator returned three P2 findings in one correction batch: null metric
keys escaping a synchronous Meter callback, logger creation racing SDK factory
disposal, and health cache age depending on the wall clock. The implementation
agent regained sole .NET runner ownership for these targeted checks. Numeric
dotnet exits were captured immediately; source/binary provenance limitations
remain as stated above.

| Run directory | Project / exact filter | Actual outcome | Numeric dotnet exit |
|---|---|---|---|
| 26-quality-boundary-red | AspNet / `FullyQualifiedName~QualityBoundaryRegressionTests` | 0 passed, 3 failed: real HTTP 500 from null Counter tag, actual ILogger disposal-race exceptions, stale healthy cache after elapsed expiry | 1 |
| 27-quality-boundary-green | AspNet / `FullyQualifiedName~QualityBoundaryRegressionTests\|FullyQualifiedName~AdversarialTelemetryTests\|FullyQualifiedName~TelemetryAdapterTests\|FullyQualifiedName~HealthBindingTests\|FullyQualifiedName~HealthNegativeMatrixTests\|FullyQualifiedName~HealthIdentityAndPermissionTests\|FullyQualifiedName~RouteCapabilityTests` | 29 passed, 0 failed | 0 |

Both runs use `console.log`, `results.trx` and `dotnet-exit-code.txt` in their own
directories. These are executed assertion failures followed by passing targeted
regressions, not compile-red evidence.

- Registry lookup now rejects null/empty/oversized keys and null/oversized values
  before hashing. The synchronous Meter bridge catches ordinary callback errors
  as safe drops. The actual Kestrel request records a null key and 100,000-character
  key/value through a real `Counter<long>`; its result remains unchanged and the
  actual SDK final metric contains the aggregate without unsafe attributes.
- The safe SDK logger is created once before its lifecycle can race disposal.
  Ordinary logging shutdown exceptions are safe drops. Four real ILogger writers
  race runtime disposal for five lifecycles, including calls after shutdown;
  this regression reproduced `ObjectDisposedException` before the fix.
- Health cache age now uses `TimeProvider.GetTimestamp`/`GetElapsedTime`. A local
  clock independently moves UTC backwards two hours and advances monotonic time.
  The real Kestrel health request keeps its valid short-lived cache, then executes
  the probe again after monotonic expiry and returns HTTP 503. No system clock
  changes or sleeps simulate the time jump; identity UTC metadata remains separate.

No unaffected identity or full contract matrix was rerun in this batch. The new
expected inventory is **55 AspNet tests and 18 Unit tests**. Parent final gates
and targeted review follow-up remain separate; no commit, push or deployment was
performed by the implementation agent.
