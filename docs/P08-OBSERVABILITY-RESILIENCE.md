# P08 observability, health, resilience, and SLO evidence

P08 status: S00-S02 complete; S03-S06 pending.

This guide is the consumer contract for the published immutable `0.8.0-alpha.2` replacement packages. The published alpha.1 artifacts remain historical evidence but are disqualified as the CRM consumer candidate because their BCL HTTP propagation still forwarded baggage. Alpha.2 defines exporter-neutral telemetry, safe operational endpoints, fail-closed outbound HTTP resilience, trace-only W3C propagation, and immutable SLO evidence. CRM consumption, locator updates, public memory, and final freeze remain separate stages.

## Install and package boundary

Consumers reference only the production packages they use, all at the same exact version:

```xml
<PackageReference Include="CP6.Platform.Contracts" Version="0.8.0-alpha.2" />
<PackageReference Include="CP6.Platform.Abstractions" Version="0.8.0-alpha.2" />
<PackageReference Include="CP6.Platform.AspNetCore" Version="0.8.0-alpha.2" />
<PackageReference Include="CP6.Platform.Messaging" Version="0.8.0-alpha.2" />
<PackageReference Include="CP6.Platform.EntityFramework" Version="0.8.0-alpha.2" />
```

`CP6.Platform.Testing` is repository-only, non-packable test support. Production projects must not reference it.

## Public API contract

| Area | Public API | Contract |
| --- | --- | --- |
| Release | `Cp6ReleaseIdentity`, `Cp6ReleaseMode`, `ICp6ReleaseIdentityAccessor` | Validated immutable service/version/Git/artifact/contract identity |
| Telemetry | `Cp6TelemetrySources`, `Cp6TelemetryMeters`, `Cp6TelemetryConventions`, `Cp6HealthTags` | Stable source, meter, operation, tag, and health names |
| Composition | `AddCp6Observability(Cp6ObservabilityProfile)` | Resource plus ASP.NET Core, HttpClient, and CP6 instrumentation |
| Health | `MapCp6OperationalEndpoints(Cp6OperationalEndpointProfile)` | Separate live, startup, ready, and release endpoints |
| Resilience | `AddCp6HttpResilience(Cp6HttpResilienceProfile)` | Explicit operation classification, bounded timeout/retry/circuit |
| Failure | `Cp6HttpResilienceException.Category` | Stable failure category without fallback or free-text mapping |
| SLO | `Cp6SloEvidenceDocument.Parse`, `Cp6SloEvidenceEvaluator.Evaluate` | Strict JSON parsing and fail-closed evidence result |

Repeated registration with the same immutable profile is safe. Conflicting registration fails during startup.

## Observability registration and exporter ownership

```csharp
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using Microsoft.Extensions.DependencyInjection;

var release = new Cp6ReleaseIdentity(
    service: "sample-api",
    version: "local",
    gitSha: "",
    artifactDigest: "",
    contractBundleDigest: "",
    mode: Cp6ReleaseMode.NonCandidate);

services.AddCp6Observability(new Cp6ObservabilityProfile(
    ServiceName: "sample-api",
    ServiceVersion: "local",
    EnvironmentName: "test",
    Region: "test-region",
    ReleaseIdentity: release));
```

Platform registers sources, meters, W3C propagation, resource identity, and instrumentation. `AddCp6Observability` constrains both the OpenTelemetry text-map propagator and the BCL `DistributedContextPropagator` used by `HttpClient` to `traceparent` and `tracestate`; it does not extract or inject baggage, `Correlation-Context`, or legacy `Request-Id`. Both propagator selections are process-wide, last-writer-wins state: register CP6 observability before constructing HTTP handlers or telemetry providers, and do not replace either global propagator later in host startup. The exporter is a **host-owned exporter**: the host selects bounded processors, exporter implementation, sampling, endpoint, and authentication outside this package. An unavailable or throwing exporter must not change the application response.

Stable ActivitySource and Meter names are `CP6.Platform.AspNetCore`, `CP6.Platform.Messaging`, and `CP6.Platform.EntityFramework`. Stable operations are:

- `cp6.http.outbound`
- `cp6.messaging.dapr.invoke`
- `cp6.messaging.publish`
- `cp6.messaging.consume`
- `cp6.outbox.dispatch`
- `cp6.inbox.process`

Metric dimensions are restricted to `cp6.region`, `cp6.operation`, `cp6.outcome`, `cp6.error.code`, `cp6.messaging.transport`, `cp6.messaging.disposition`, and `cp6.http.operation_kind`. Do not add identities, correlation/event/trace IDs, resource IDs, raw routes, query strings, payloads, exception messages, or infrastructure names as metric tags.

## Health and release registration

The consumer owns dependency checks and selects stable public component names. Platform does not read connection configuration or discover dependencies.

```csharp
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

builder.Services.AddHealthChecks()
    .AddCheck("configuration", () => HealthCheckResult.Healthy(), tags: [Cp6HealthTags.Startup])
    .AddCheck("required-dependency", () => HealthCheckResult.Healthy(), tags: [Cp6HealthTags.Ready]);

app.MapCp6OperationalEndpoints(new Cp6OperationalEndpointProfile(
    ["configuration", "required-dependency"]));
```

| Endpoint | Meaning | Result |
| --- | --- | --- |
| `/health/live` | Process/event-loop liveness only | 200 without external dependency checks |
| `/health/startup` | Required configuration and initial load | 200 only when tagged checks are Healthy; otherwise 503 |
| `/health/ready` | Consumer-selected required dependencies | 200 only when tagged checks are Healthy; otherwise 503 |
| `/health/release` | Validated release identity | 200 when identity matches registration; otherwise 503 |

Responses use `Cache-Control: no-store` and expose only schema version, overall status, UTC observation time, allowlisted component name/status, and safe release identity. Health detail dictionaries and exception text are never emitted.

`Candidate` requires canonical SemVer, a 40-character lowercase Git SHA, and canonical `sha256:` artifact and contract-bundle digests. `NonCandidate` permits explicit local/test identity and always exposes `candidate=false`.

## HTTP resilience decision table

| Operation kind | Allowed methods | Retry rule | Missing classification/key |
| --- | --- | --- | --- |
| `IdempotentRead` | GET, HEAD, OPTIONS | Up to the bounded profile count for approved transient outcomes | Other methods fail with `OperationNotAllowed` |
| `IdempotentWrite` | POST, PUT, PATCH, DELETE | Only with exactly one valid `Idempotency-Key` | Missing/invalid key fails with `IdempotencyRequired` |
| `NonIdempotent` | POST, PUT, PATCH, DELETE | Never retried, regardless of configured retry count | Read methods fail with `OperationNotAllowed` |
| Unclassified | None | No permissive default | Registration is invalid |

Approved transient outcomes are transport failures and HTTP 408, 429, 500, 502, 503, or 504. Cancellation propagates immediately. Stable categories are `OperationNotAllowed`, `IdempotencyRequired`, `AttemptTimeout`, `TotalTimeout`, and `CircuitOpen`. There is no hedging, automatic fallback, unbounded retry, or synthetic success.

## HTTP and CloudEvent trace propagation

ASP.NET Core and HttpClient instrumentation form W3C server/client spans. P03 correlation remains a separate support identifier: it is neither derived from nor substituted by a trace ID. The outbound handler removes conflicting correlation values and copies only a validated current value; a background call without one receives a fresh safe value.

P04 CloudEvents retain their seven required business extensions. `traceparent` and `tracestate` are optional diagnostics; `baggage` is not propagated. Invalid, duplicate, or overlong trace context is discarded and processing starts a fresh root. Telemetry cannot authorize identity, bypass P04/P05/P06 validation, or alter topic, partition key, payload, lease, transaction, idempotency, retention, or DLQ semantics.

## SLO evidence

The stable Draft 2020-12 schema ID is `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json`; schema version is `1.0.0`. The schema and examples are owned only by `CP6.Platform.Contracts`.

Use `Cp6SloEvidenceDocument.Parse` for strict size, duplicate-property, unknown-member, timestamp, digest, and result validation. Use `Cp6SloEvidenceEvaluator.Evaluate` to reproduce the fail-closed result. `Pass` requires a Candidate release, complete full-coverage window, samples, valid and mutually matching definition/release/query/evidence digests, verified exclusions, and a measurement meeting the threshold. Otherwise the result is `Fail` or `Indeterminate`.

All repository examples are synthetic and state `productionSloClaimed=false`. S01 does not establish a production SLO.

## Deterministic test support

Repository tests use `Cp6TelemetryRecorder` for trace topology, allowed-tag, and forbidden-data assertions. `Cp6HttpFaultScript` provides ordered success, status, exception, and delay outcomes. `AddCp6HttpFaultInjection` accepts only exact `Test` or `CI` environment names and fails before mutating services in any other environment.

Consumer black-box tests should exercise public production APIs with consumer-owned fixtures. They must not copy repository test source or add a cross-repository project reference to `CP6.Platform.Testing`.

## Non-goals

S01 does not deploy OpenTelemetry Collector, Prometheus, Grafana, Tempo, a SaaS backend, dashboards, alert routes, infrastructure resources, environment networking, or a CRM Worker. It does not create CRM subscriptions/routes, publish immutable packages, consume them in CRM, claim production capacity, perform P09 provisioning, perform P10 signing/System Manifest reconciliation, or execute a production deployment.

Operational response procedures are in the five P08 runbooks. Publication status and evidence boundaries are in `P08-PUBLICATION.md`.
