# P08-S01 completion and publication readiness

P08 status: S00-S01 complete; S02-S06 pending.

## Decision

S01 is complete on Platform `main`. This is not package-publication evidence, not CRM consumption evidence, and not a `Frozen / Consumable` declaration. S02-S06 remain required.

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

The local Docker Desktop backend crashed before its Linux engine became available because a stale inference runtime socket could not be accessed. Therefore the P05 Dapr/Kafka and P06 SQL scripts could not produce valid local test results. No Docker image, volume, or repository data was reset or removed. The required remote Ubuntu jobs supplied the authoritative real-profile evidence recorded below.

## Cross-service and regression evidence

The two-host fixture proves one W3C server/client/server trace with distinct spans and release resources, independent correlation, no baggage, fresh roots for malformed/duplicate/overlong context, safe telemetry, caller cancellation, exporter isolation, retry boundaries, and circuit recovery. P04 event fields, P05 topic/key ordering, P06 transaction/lease/idempotency/DLQ behavior, and P07 gateway E2E remain covered by the full gates.

## Stage ledger

| Stage | Status | Exit condition |
| --- | --- | --- |
| S00 | Complete | Approved design and implementation plan merged to Platform main |
| S01 | Complete | Platform PR #19 merged; Windows/Linux, real Dapr/Kafka, and real SQL main jobs green |
| S02 | Pending | Exact-main immutable package publication and artifact/digest evidence |
| S03 | Pending | CRM fixed-version consumption through CRM PR and main CI |
| S04 | Pending | Package locator and source-of-truth reconciliation |
| S05 | Pending | Public project-memory and changelog reconciliation |
| S06 | Pending | Final evidence audit and `Frozen / Consumable` decision |

## Immutable PR and main evidence

- Producer PR: #19, merged.
- Producer head: `c13e1f2a2e7efcd48c999592b01ece7686036473`.
- Platform merge SHA: `8b8598a1d24f3d465e83c83f5c44353c951856c7`.
- Main workflow: [platform-validation run 33303723733](https://github.com/GTX537/CP6.Platform/actions/runs/33303723733), exact head `8b8598a1d24f3d465e83c83f5c44353c951856c7`.
- `ubuntu-latest` job `99236432505`: Success.
- `windows-latest` job `99236432580`: Success.
- `ubuntu-sql-server` job `99236432586`: Success.
- `ubuntu-dapr-kafka` job `99236432626`: Success.

All required S01 jobs concluded successfully. S02 package publication may now begin from this exact Platform main baseline; no later commit may be substituted without rerunning the S02 authority checks.

## Stable contract identifiers

Operations are `cp6.http.outbound`, `cp6.messaging.dapr.invoke`, `cp6.messaging.publish`, `cp6.messaging.consume`, `cp6.outbox.dispatch`, and `cp6.inbox.process`. Stable HTTP failures are `OperationNotAllowed`, `IdempotencyRequired`, `AttemptTimeout`, `TotalTimeout`, and `CircuitOpen`. The SLO schema ID is `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json`.
