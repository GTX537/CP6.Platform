# P08-S01 publication readiness

P08 status: S00 complete; S01 implementation candidate; S02-S06 pending.

## Decision

This branch is an implementation candidate for Platform review. It is not package-publication evidence, not CRM consumption evidence, and not a `Frozen / Consumable` declaration. S01 becomes complete only after its Platform PR is merged and the exact `main` commit passes every required remote job.

## Scope

- Exporter-neutral OpenTelemetry resource, ASP.NET Core, HttpClient, messaging, Outbox, and Inbox instrumentation.
- Safe `/health/live`, `/health/startup`, `/health/ready`, and `/health/release` contracts.
- Explicit HTTP operation kinds with bounded retry, timeout, circuit breaking, cancellation, and stable failures.
- Optional W3C CloudEvent propagation while preserving P04-P06 validation and transactional semantics.
- Versioned SLO evidence schema/evaluator and repository-only deterministic test utilities.
- Exact five runtime packages plus five symbol packages, entry-level reproducibility, asset ownership, and package-content safety checks.

## Exclusions

The candidate does not provision OpenTelemetry Collector, exporter backends, dashboards, alerts, infrastructure, environment routes, CRM Worker processes, CRM subscriptions, or deployment assets. The exporter remains a host-owned exporter. It does not claim a production SLO; examples use `productionSloClaimed=false`.

## Frozen dependency and package baseline

| Dependency | Exact version |
| --- | --- |
| `OpenTelemetry.Extensions.Hosting` | `1.18.0` |
| `OpenTelemetry.Instrumentation.AspNetCore` | `1.18.0` |
| `OpenTelemetry.Instrumentation.Http` | `1.18.0` |
| `Microsoft.Extensions.Http.Resilience` | `10.9.0` |

Candidate package version is `0.8.0-alpha.1`. The approved runtime set is Contracts, Abstractions, AspNetCore, Messaging, and EntityFramework. `CP6.Platform.Testing` is repository-only and excluded from pack/publish evidence.

## Local evidence at PR checkpoint

- Format: Passed.
- Build: Passed with zero warnings and zero errors.
- Unit: Passed, 124 tests; the failure-evidence self-test also passed.
- Integration: Passed, 136 ASP.NET Core tests including nine two-host observability/resilience cases.
- E2E: Passed, 31 gateway and P08 observability tests.
- Contract: Passed, including Architecture 9/9, two independently packed sets, exact ten package files, non-empty runtime assemblies, P04/P08 asset ownership, content safety, and entry SHA comparison.
- Security: Passed with direct and transitive dependency audit across all projects.
- Performance: NotApplicable with explicit P08-S01 reason and machine evidence.
- Migration: NotApplicable with explicit P08-S01 reason and machine evidence.
- Diff/format/failure contracts: Passed.

The local Docker Desktop backend crashes before its Linux engine becomes available because a stale inference runtime socket cannot be accessed. Therefore the P05 Dapr/Kafka and P06 SQL scripts cannot produce valid local test results. No Docker image, volume, or repository data was reset or removed. Both profiles remain mandatory remote Ubuntu jobs and must be green before merge; a skipped, cancelled, stale, or red result is not acceptable evidence.

## Cross-service and regression evidence

The two-host fixture proves one W3C server/client/server trace with distinct spans and release resources, independent correlation, no baggage, fresh roots for malformed/duplicate/overlong context, safe telemetry, caller cancellation, exporter isolation, retry boundaries, and circuit recovery. P04 event fields, P05 topic/key ordering, P06 transaction/lease/idempotency/DLQ behavior, and P07 gateway E2E remain covered by the full gates.

## Stage ledger

| Stage | Status | Exit condition |
| --- | --- | --- |
| S00 | Complete | Approved design and implementation plan merged to Platform main |
| S01 | Implementation candidate | Platform PR merged; Windows/Linux, real Dapr/Kafka, and real SQL main jobs green |
| S02 | Pending | Exact-main immutable package publication and artifact/digest evidence |
| S03 | Pending | CRM fixed-version consumption through CRM PR and main CI |
| S04 | Pending | Package locator and source-of-truth reconciliation |
| S05 | Pending | Public project-memory and changelog reconciliation |
| S06 | Pending | Final evidence audit and `Frozen / Consumable` decision |

## PR and main evidence policy

At this candidate checkpoint, S01 PR number, merge SHA, and main workflow run are intentionally absent because they do not yet exist. After merge, record those immutable identifiers on a separate evidence branch when repository history requires it. Package publication cannot begin until that record proves all required jobs concluded successfully.

## Stable contract identifiers

Operations are `cp6.http.outbound`, `cp6.messaging.dapr.invoke`, `cp6.messaging.publish`, `cp6.messaging.consume`, `cp6.outbox.dispatch`, and `cp6.inbox.process`. Stable HTTP failures are `OperationNotAllowed`, `IdempotencyRequired`, `AttemptTimeout`, `TotalTimeout`, and `CircuitOpen`. The SLO schema ID is `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json`.
