# WP02-25 local observability V1

This is an explicit opt-in adapter in `CP6.Platform.Contracts.ObservabilityV1` and
`CP6.Platform.AspNetCore.ObservabilityV1`. The contract version is `1.0.0` within
that namespace. It does not change the old `Cp6ReleaseIdentity`, SLO evaluator or
SLO 1.0.0 JSON, the legacy observability registration, or `/health/release`.

## Identity boundary

`RuntimeIdentityBindingV1` contains a copied declared release identity and bounded
environment, region, package-set reference, configuration digest, schema-set
reference, deployment reference, process instance/epoch/start time and freshness.
These are **host configuration declarations**, not independent attestations.

`RuntimeManifestV1` is loaded from a separately approved/pinned configuration
channel. Its bounded, unique module inventory contains module name, assembly
version, SHA-256 and optional source revision. Never construct expected pins from
the observer's output in a real consumer. Fixture tests intentionally generate
local pins and are not release provenance evidence.

`OwnedModuleObserverV1` copies module bytes, then loads those same owned bytes via
a collectible `AssemblyLoadContext.LoadFromStream`. It reads name, version and
canonical source revision from the loaded assembly's metadata. It hashes its own
bytes, not an on-disk file that could have changed after loading. Caller byte-array
mutation cannot alter the observation. It does not use MVID as a content hash.
At most 32 modules, 16 MiB each and 32 MiB total can be loaded.

`Match` means **only the exact owned managed modules loaded by this observer**
match that pinned inventory and declared host binding. The observer does not
prove that a business endpoint executes those modules, that arbitrary previously
loaded/default-context modules have those bytes, or that native dependencies,
process/container image, package assets or the whole running host match. Unsupported
artifact kinds, absent/disposed observers, clock errors, cancellation and stale
observations are `Unproven`; differing binding/pin/inventory is `Mismatch`.
An arbitrary observer implementation or two caller DTOs cannot mint a positive
observation: the evaluator accepts only the sealed byte-owning loader.

The source revision is read from `AssemblyInformationalVersionAttribute` metadata,
never copied from the expected manifest or the declared host source SHA. Metadata
can be self-reported by a build and is not source provenance. A requested source
pin must match; an absent source pin does not turn metadata into trusted evidence.
Host source and individual package/module sources may legitimately differ.

The observer validates its own timestamp before loading modules: nonzero offsets,
epoch/minimum timestamps and clock exceptions cannot mint evidence. Evaluation
also rejects future observation times. Freshness is rechecked on **every response**, against the original byte-load
observation time using both UTC metadata and the observer's original monotonic clock.
The evaluator retains expiry once either time boundary is exceeded; UTC rollback,
a frozen wall clock or a different evaluator clock cannot renew stale evidence.
This adapter deliberately does not renew stale proof merely
because it can still read a file or compare cached declarations. A consumer must
create a new independently bound observation when its freshness window expires;
this local adapter is not a long-lived production attestation service.

```csharp
// Both are host-owned input channels. Do not derive expectedManifest from observer.
RuntimeIdentityBindingV1 actualHostConfiguration = ReadActualHostConfiguration();
RuntimeManifestV1 expectedManifest = ReadSeparatelyPinnedManifest();
using var observer = new OwnedModuleObserverV1(
    actualHostConfiguration, "owned-loader", ReadModuleByteImages());
var identity = new RuntimeIdentityEvaluatorV1(expectedManifest, observer);
var localObservation = identity.Observe();
```

The public safe observation has no file paths or exception text. Current package
trust, source provenance and stronger deployment attestation remain `Unproven`.
`ObservationEnvelopeV1` is always `local-nonproduction`: declared identity, runtime
observation, current package trust, host registration, business acceptance and
independent qualification are separate statuses. Candidate flags, source hashes,
a valid-looking seven-package lock or matching DI cannot set production acceptance.

## Health registration

Register actual `IHealthCheck` instances and an explicit permission policy before
mapping the V1 endpoints. Each `HealthCheckBindingV1` names the actual registration,
safe output name, purpose, required tag, dependency kind, timeout, freshness,
read-only probe policy and whether to publish its individual result. Empty,
duplicate, missing, wrong-tag or absent-policy registration is rejected. Startup
and ready with no checks for that purpose never report healthy.

```csharp
builder.Services.AddAuthorization(options => options.AddPolicy("operator",
    policy => policy.RequireClaim("permission", "ops.read")));
builder.Services.AddHealthChecks().AddCheck("dependency", readOnlyDependencyCheck,
    tags: new[] { "ready-v1" });
builder.Services.AddCp6HealthV1(new HealthProfileV1("operator",
    new[] {
        new HealthCheckBindingV1("dependency", "dependency", HealthPurposeV1.Ready,
            "ready-v1", DependencyKindV1.RemoteService, TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2), ProbePolicyV1.ReadOnly, publish: false)
    }, new[] {
        new CapabilityRouteV1("/orders/{id}", CapabilityV1.Read,
            requiredChecks: new[] { "dependency" }),
        new CapabilityRouteV1("/orders", CapabilityV1.NewEffect, "Orders.Module",
            requiredChecks: new[] { "dependency" })
    }), identity);
// Configure the application's authentication handler as usual.
var app = builder.Build();
app.MapCp6HealthV1();
```

The authenticated operational routes are `/ops/v1/live`, `/ops/v1/startup`,
`/ops/v1/ready` and `/ops/v1/identity`. Live is constant and never runs dependency
checks. Existing route collisions, including catch-all routes, are rejected;
the complete route table is checked again before the server starts listening.
The capability inventory describes consumer routes; it does not install those
business routes or bypass their authorization.

Every route has its own explicit, defensively copied required-check inventory.
Missing, empty, duplicate or unknown inventories, and references to startup checks
instead of readiness checks, are rejected. A write-only dependency failure cannot
disable a read route that has different healthy dependencies. Each new-effect
route also identifies its relevant owned module; its availability compares that
module's independent pin result, rather than using the whole-inventory verdict.
A mismatch in module B blocks B's route while a matching module A route remains
available. A common host/source binding mismatch blocks all affected effect routes.

The response `Routes` lists each route's availability, module verdict and safe
dependency failure classification. `ReadAvailable`/`NewEffectsAvailable` mean that
at least one route in that class is available. The overall `Healthy` aggregate
remains conservative: every required check and route must pass, and any required
effect capability also requires the whole pinned inventory to match. Thus a hidden
dependency or module failure keeps HTTP 503 even when an independent read or effect
route remains available. Startup reports its own required checks, with no route
availability grant. This is an operational
availability report, **not a write-permission API** or production adoption claim.
Removing sensitive component entries does not remove their aggregate failure.
Exception text, descriptions and arbitrary health data are not serialized.

Checks must actually be read-only. This adapter cannot make arbitrary consumer
`IHealthCheck` code safe merely by labeling it read-only. Consumer review must
exclude migrations, replay, cleanup, Owner writes and other side effects; no such
operations are implemented or invoked by this adapter. Local tests use loopback
HTTP and isolated deterministic probes only, never a database.

There are at most eight in-flight probe invocations process-wide, including
uncooperative work from stopped hosts. Each check has one retained pending task.
A synchronous factory/check runs outside the request; timeout/cancellation never
releases its slot until the actual delegate returns. Repeated requests cannot
create replacement work for a blocked check. Cache results are bounded per
registered check and expire using monotonic elapsed timestamps, separately from
UTC identity metadata. Wall-clock rollback cannot extend a healthy cache entry.
The runtime uses the registered `TimeProvider`, or `TimeProvider.System` by default;
the rollback regression supplies a local controllable clock without changing the
machine's clock. Timeouts, cancellation,
failure and busy state are never converted into healthy.

## Privacy and SDK boundary

```csharp
var localSink = new LocalTelemetrySinkV1(capacity: 1024);
builder.Services.AddCp6ObservabilityV1(
    new TelemetryPolicyV1("orders", "test", "east",
        new[] { "/orders/{id}", "/orders" }),
    localSink,
    new TelemetryLimitsV1(queueCapacity: 256, maximumRecords: 1024,
        batchSize: 16, recordsPerSecond: 512, maximumSeries: 128));
```

Registration creates private OpenTelemetry providers using the installed SDK.
ASP.NET Core and HttpClient automatic instrumentation are active. Actual inbound
requests are bound to their hosting runtime by startup middleware; each trace
processor only exports its runtime's activities. Request/background CP6 spans use
the existing CP6 sources. Background consumers must enter
`using (runtime.BeginScope())` around the observed work so it has an explicit host.
No global provider filtering mutates other hosts' Activity sampling decisions.

Only the exact key **and** an explicitly registered finite string value can pass.
Safe dimensions include known method, approved route template, response class and
finite CP6 enums. Resources consist only of the configured singleton service,
environment and region values. Unknown keys/resources, case variants, raw URLs,
query/login codes, Authorization/Cookie/body/SQL values, tenant/user/message/
operation IDs, arbitrary baggage, exceptions, events, links and status descriptions
are omitted. Original display names are replaced by finite operation names.

The three concrete paths are:

1. Actual SDK `Activity` -> synchronous safe projection -> bounded safe queue.
   The adapter never queues or passes the raw Activity to its sink.
2. Existing CP6 `Meter` -> bounded `MeterListener` validation/projection **before
   aggregation** -> private sanitized Meter -> actual OpenTelemetry metric reader
   and exporter -> safe queue. The SDK aggregates finite series; final exports are
   observed in tests. No raw metric dimensions reach this private SDK aggregator.
3. Registered `ILoggerProvider` -> finite severity and constant event **before
   the SDK sees a log** -> private OpenTelemetry logger/processor -> safe queue.
   Original category, template, arguments, state, scope and exception are not
   enumerated or formatted. Arbitrary user `ToString`/formatter methods are not run.

SDK instrumentation can transiently create raw in-process Activity fields. This
guarantee covers this registration's queue, aggregations and final local sink;
it cannot make unrelated listeners, providers, exporters or application logging
safe. It does not remove manually authored HTTP headers from business requests.
Automatic propagation installs trace-context-only propagators and never propagates
baggage. Multiple V1 hosts share a reference-counted lease; global replacement
causes fail-closed export and rejects a further registration while active. Final
disposal restores only values still owned by V1, preserving an external replacement.

External export is always disabled. No network destination, retention/permission
proof, credential or exporter is configured. The only sink is bounded local
process memory, retained until overwritten or the sink is released.

## Bounds and completeness

Input dimension enumeration is limited to 32, output attributes to 12, names/values
to closed bounded registries and final records to a shape below 4096 UTF-8 bytes.
Null keys, keys longer than 64 characters and values longer than 128 characters
are rejected before any registry hash lookup. An ordinary synchronous Meter
callback failure becomes a safe drop rather than escaping from `Counter.Add`.
The SDK logger is created before shutdown can race logging; ordinary logging
shutdown failures also become safe drops, without formatting the caller's data.
Queue, lifetime accepted-record budget, rate, SDK series, batches and local retained
records have independent configured limits. Oversized/nonfinite/negative
measurements are rejected. Durations are bounded at one hour. Measurement rate
and series checks run before SDK aggregation. The local queue never waits for a
consumer on the request thread and never retries a failed sink.

Only four sink invocations may be in flight process-wide. A synchronously blocked
sink retains its slot even after host disposal; additional hosts cannot spawn
unbounded abandoned export work. Queue shutdown waits at most 200 ms. Flush accepts
an explicit budget of at most ten seconds and does not claim delivery on failure.
Safe drop/overwrite counts are exposed, with no raw reason or exception payload.
The lifetime maximum-record budget does not reset when the queue drains.

`Completeness` is always `Incomplete`: there is no trusted denominator, coverage
proof or independent SLO qualification. A full queue, blocked/throwing sink,
sampling or missing records never become “100% coverage.” Telemetry errors do not
change the fixture's HTTP result or simulated Owner counter. Those fixture counters
are not production Owner commits.

## Metric meanings and acceptance limits

`MetricDefinitionV1` records version, unit, kind, measurement point, commit phase,
attempt/business scope, finite policy, process reset, aggregation, owner and source.
Existing technical names keep their original meanings: `cp6.messaging.published`
observes transport return; `cp6.messaging.consumed` observes delivery validation;
`cp6.outbox.operations` with `enqueue` may precede the caller's commit. The latter
is deliberately labeled `BeforeCommit`; none proves an Owner commit.

`OwnerReceiptLatencyV1` is a separate receipt-pair contract. Producer committed and
consumer applied receipts must name the same message, different Owners and the
same explicit clock provenance, with UTC times. Missing/wrong/retry/duplicate
receipts are `NotMeasured`; incompatible clocks are `ClockUnproven`, negative elapsed
time is `ClockSkew`, and no unavailable measurement is represented as zero. Receipt
authenticity and cross-call deduplication remain the consumer Owner's responsibility.
The adapter has no real Owner receipt source and does not emit this business latency
from transport counters. Positive receipt tests are synthetic contract fixtures.

WP02-26 owns SLO qualification/calculation/general recorder work. WP02-28/29 own
actual consumer-host adoption. Local loopback tests, loaded fixture modules,
synthetic receipt pairs, source metadata and passing tests are not production,
independent package provenance, real Owner acceptance or original-AC execution.
The original 32 acceptance cases remain NOT_RUN in their original evidence scope.

Targeted implementation logs, intermediate failures and actual fixture output are
under `docs/development/evidence/WP02-25/20261014T052841Z/implementation/`.
The implementation execution ledger records the limits of intermediate command
exit/source evidence; parent delivery gates pin the final source and binaries.
