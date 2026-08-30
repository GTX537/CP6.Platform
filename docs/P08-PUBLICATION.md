# P08-S02 immutable package publication evidence

P08 status: S00-S02 complete; S03-S06 pending.

## Remediation decision

The immutable `0.8.0-alpha.1` packages remain historical publication evidence but are disqualified as the CRM consumer candidate: the real downstream request still received baggage because only the OpenTelemetry propagator, not the BCL `HttpClient` propagator, was constrained. The forward-only replacement `0.8.0-alpha.2` was published from exact Platform main and independently verified without overwriting or deleting alpha.1. S02 is complete again; CRM fixed-version consumption remains S03.

## Prior alpha.1 publication decision

Five immutable `0.8.0-alpha.1` runtime packages were published from one exact, validated main commit and their complete artifact evidence was independently verified. That evidence remains valid for what was published, but alpha.1 is not eligible for CRM consumption and does not support a `Frozen / Consumable` declaration.

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

The superseded published version is `0.8.0-alpha.1`; the published forward replacement is `0.8.0-alpha.2`. The approved runtime set remains Contracts, Abstractions, AspNetCore, Messaging, and EntityFramework. `CP6.Platform.Testing` is repository-only and excluded from pack/publish evidence.

## Local evidence at PR checkpoint

- Format: Passed.
- Build: Passed with zero warnings and zero errors.
- Unit: Passed, 124 tests; the failure-evidence self-test also passed.
- Integration: Passed, 142 ASP.NET Core tests including direct propagation contracts and two-host observability/resilience cases.
- E2E: Passed, 31 gateway and P08 observability tests.
- Contract: Passed, including Architecture 10/10, two independently packed sets, exact ten package files, non-empty runtime assemblies, P04/P08 asset ownership, content safety, and entry SHA comparison.
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
| S02 | Complete | Exact-main `0.8.0-alpha.2` published and independently verified without altering alpha.1 |
| S03 | Pending | CRM fixed-version consumption through CRM PR and main CI |
| S04 | Pending | Package locator and source-of-truth reconciliation |
| S05 | Pending | Public project-memory and changelog reconciliation |
| S06 | Pending | Final evidence audit and `Frozen / Consumable` decision |

## Alpha.2 remediation PR and exact-main evidence

- Remediation PR: [Platform PR #23](https://github.com/GTX537/CP6.Platform/pull/23), merged.
- Remediation head: `14b9bc2d41c8446f2c236094b9f2d82f1124d045`.
- PR workflow: [platform-validation run 33320438234](https://github.com/GTX537/CP6.Platform/actions/runs/33320438234), exact head `14b9bc2d41c8446f2c236094b9f2d82f1124d045`.
- PR `ubuntu-latest` job `99281335480`: Success.
- PR `windows-latest` job `99281335516`: Success.
- PR `ubuntu-dapr-kafka` job `99281335324`: Success.
- PR `ubuntu-sql-server` job `99281335536`: Success.
- Exact publication source and remediation merge SHA: `bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672`.
- Exact post-merge workflow: [platform-validation run 33320608737](https://github.com/GTX537/CP6.Platform/actions/runs/33320608737).
- Main `ubuntu-latest` job `99281796991`: Success.
- Main `windows-latest` job `99281797092`: Success.
- Main `ubuntu-dapr-kafka` job `99281797105`: Success.
- Main `ubuntu-sql-server` job `99281797120`: Success.

The remote Platform main remained exactly `bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672` through the terminal publication observation. The remediation adds a trace-only BCL adapter, independent legacy `Request-Id`/baggage contract tests, and a real two-Kestrel regression without adding public package API.

## Alpha.2 immutable publication run

- Publisher: [publish-alpha run 33320840180](https://github.com/GTX537/CP6.Platform/actions/runs/33320840180).
- Event/ref/head: `workflow_dispatch`, `main`, `bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672`.
- Publish job `99282412376`: Success; started `2026-08-30T15:52:06Z` and completed `2026-08-30T15:56:27Z`.
- The exact-main assertion, Format/Build/Unit/P05/P06/E2E/Contract/Security verification, ten-file pack, five ordinary package pushes, and evidence upload all succeeded.
- Push logs recorded one successful upload each for Abstractions, AspNetCore, Contracts, EntityFramework, and Messaging. Symbols were retained in evidence and were not pushed.
- The workflow used only GitHub Packages source `nuget.pkg.github.com/GTX537`, did not use duplicate skipping, and did not expose or persist its short-lived package token.

## Alpha.2 artifact identity and retention

| Field | Exact value |
| --- | --- |
| Artifact ID | `9734883916` |
| Name | `p08-alpha-bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672` |
| API digest | `sha256:db2e44481101dcf450cd1a0d6188572ac8c1529fc148e6ea3d094d8c772a4e61` |
| Size | `842270` bytes |
| Created | `2026-08-30T15:56:25Z` |
| Expires | `2026-09-29T15:56:24Z` |
| Retention policy | 30 days |

The API digest is GitHub-computed artifact metadata. Independent verification used the extracted package files and their embedded `sha256.json`; it did not claim to reproduce the server-side artifact archive digest.

## Alpha.2 exact package ledger

| File | Bytes | SHA-256 | Registry status |
| --- | ---: | --- | --- |
| `CP6.Platform.Abstractions.0.8.0-alpha.2.nupkg` | 8630 | `842b007d3b8e7c369f7de3e22d03d2eb46746e14ad809ce5b9ce4e01ee1114ca` | Published |
| `CP6.Platform.Abstractions.0.8.0-alpha.2.snupkg` | 9674 | `55c88e13645e450b41c3e9cdc75fb51d76a080fe6d046dc62a81e1c837843f7c` | Artifact only |
| `CP6.Platform.AspNetCore.0.8.0-alpha.2.nupkg` | 39823 | `3564b21bd621307e002d1307c8081062e9c3743533fd4f0dac4ef7bed9ada92c` | Published |
| `CP6.Platform.AspNetCore.0.8.0-alpha.2.snupkg` | 26721 | `2ac3ea03f8f20d75b294a60068e389fc9f4f80cea172e27607c8d2c8b049da1e` | Artifact only |
| `CP6.Platform.Contracts.0.8.0-alpha.2.nupkg` | 31396 | `b6a0106bdc8c60f0c49ed29263f8e538beb3ac9ad2d181227d40e7e2a8e213df` | Published |
| `CP6.Platform.Contracts.0.8.0-alpha.2.snupkg` | 24279 | `63e771d37a0b1f0f237176d104ffc67983d36ec803355a039ec8310c3f736a04` | Artifact only |
| `CP6.Platform.EntityFramework.0.8.0-alpha.2.nupkg` | 44178 | `d1e7cf733693e13b34a8cb39a64077c498cc4fbb082953a5e7a0b0cb27d1acee` | Published |
| `CP6.Platform.EntityFramework.0.8.0-alpha.2.snupkg` | 16815 | `2f8ef40d20be7748a1880263d30c1e12bf6ddfc39a4c2fcb37b963687f8591a2` | Artifact only |
| `CP6.Platform.Messaging.0.8.0-alpha.2.nupkg` | 44517 | `64d5878567213cfde658117e069f3e598ecbd53dbb8e0ef4cfd19ceca314b7a8` | Published |
| `CP6.Platform.Messaging.0.8.0-alpha.2.snupkg` | 24790 | `648be0f981df757e79dff180536ce6837dceb1b51f33b7eb0123df63b1e0e632` | Artifact only |

The downloaded manifest contained exactly these ten filenames. Every independently recomputed SHA-256 matched, every ordinary package contained its non-empty `lib/net8.0/<PackageId>.dll`, and every nuspec bound repository commit `bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672` with version `0.8.0-alpha.2`.

## Alpha.2 Registry identities

| Package | GitHub Packages version ID | Created/updated UTC |
| --- | ---: | --- |
| `CP6.Platform.Abstractions` | [`1188299233`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Abstractions/1188299233) | `2026-08-30T15:56:19Z` |
| `CP6.Platform.AspNetCore` | [`1188299259`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.AspNetCore/1188299259) | `2026-08-30T15:56:20Z` |
| `CP6.Platform.Contracts` | [`1188299302`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Contracts/1188299302) | `2026-08-30T15:56:21Z` |
| `CP6.Platform.EntityFramework` | [`1188299341`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.EntityFramework/1188299341) | `2026-08-30T15:56:22Z` |
| `CP6.Platform.Messaging` | [`1188299373`](https://github.com/users/GTX537/packages/nuget/CP6.Platform.Messaging/1188299373) | `2026-08-30T15:56:23Z` |

Each package query returned exactly one `0.8.0-alpha.2` version. Alpha.1 remains present and unchanged as disqualified historical evidence.

## Alpha.2 retained and independently verified evidence

- Artifact roots were exactly `release`, `verify`, `p05-integration`, and `p06-sql-integration`, with 71 files total: 11 release files, 56 verification files, and two files for each real integration profile.
- All seven retained gate summaries reported `Passed` and exact commit `bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672`; the Contract summary retained passed `ContractBundlePackageContent`, `SloEvidencePackageContent`, `PackageContentSafety`, and `PackageReproducibility` checks.
- Archive inspection found no Testing namespace/assets, preserved P08 assets only in Contracts and P04 event assets only in Messaging, and found no machine-specific package entry paths.
- P05 `result.json` reported `Passed` with Dapr `1.18.2`, Kafka `4.3.1`, valid service invocation, and a contract-valid partitioned `evt-0001`; its compose log was non-empty.
- P06 `result.json` reported `Passed` with SQL Server `2022-CU26-ubuntu-22.04`, image digest `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`, all seven transactional checks, and a non-empty SQL log.

## S01 immutable PR and main evidence

- Producer PR: #19, merged.
- Producer head: `c13e1f2a2e7efcd48c999592b01ece7686036473`.
- Platform merge SHA: `8b8598a1d24f3d465e83c83f5c44353c951856c7`.
- Main workflow: [platform-validation run 33303723733](https://github.com/GTX537/CP6.Platform/actions/runs/33303723733), exact head `8b8598a1d24f3d465e83c83f5c44353c951856c7`.
- `ubuntu-latest` job `99236432505`: Success.
- `windows-latest` job `99236432580`: Success.
- `ubuntu-sql-server` job `99236432586`: Success.
- `ubuntu-dapr-kafka` job `99236432626`: Success.

All required S01 jobs concluded successfully and supplied the implementation baseline for S02.

## S02 planning PR and exact-main evidence

- Planning PR: [Platform PR #21](https://github.com/GTX537/CP6.Platform/pull/21), merged.
- Planning head: `e8e37c28133578a0ae8c593c36f324111909e58a`.
- Planning PR run: `33305003514`.
- PR `ubuntu-latest` job `99239860544`: Success.
- PR `windows-latest` job `99239860526`: Success.
- PR `ubuntu-dapr-kafka` job `99239860555`: Success.
- PR `ubuntu-sql-server` job `99239860588`: Success.
- Exact publication source and planning merge SHA: `b065c3e5d432c9c4cd8ceaa5346b5b52cc148e5f`.
- Exact post-merge workflow: [platform-validation run 33305166884](https://github.com/GTX537/CP6.Platform/actions/runs/33305166884).
- Main `ubuntu-latest` job `99240294789`: Success.
- Main `windows-latest` job `99240294790`: Success.
- Main `ubuntu-dapr-kafka` job `99240294871`: Success.
- Main `ubuntu-sql-server` job `99240294681`: Success.

The remote Platform main remained exactly `b065c3e5d432c9c4cd8ceaa5346b5b52cc148e5f` from final authorization through the terminal publication observation.

## Immutable publication run

- Publisher: [publish-alpha run 33305345694](https://github.com/GTX537/CP6.Platform/actions/runs/33305345694).
- Event/ref/head: `workflow_dispatch`, `main`, `b065c3e5d432c9c4cd8ceaa5346b5b52cc148e5f`.
- Publish job `99240789258`: Success; started `2026-08-30T09:58:58Z` and completed `2026-08-30T10:03:20Z`.
- The exact-main assertion, release verification, ten-file pack, five ordinary package pushes, and evidence upload all succeeded.
- Push logs recorded one successful upload each for Abstractions, AspNetCore, Contracts, EntityFramework, and Messaging. Symbols were retained in evidence and were not pushed.
- The workflow used only GitHub Packages source `nuget.pkg.github.com/GTX537`, did not use duplicate skipping, and did not expose or persist its short-lived package token.

## Artifact identity and retention

| Field | Exact value |
| --- | --- |
| Artifact ID | `9730322914` |
| Name | `p08-alpha-b065c3e5d432c9c4cd8ceaa5346b5b52cc148e5f` |
| API digest | `sha256:fb4b4e0c458780a94098103fa82249facaddb81e84f00369f8097e44a3e341ae` |
| Size | `838134` bytes |
| Created | `2026-08-30T10:03:18Z` |
| Expires | `2026-09-29T10:03:18Z` |
| Retention policy | 30 days |

The API digest is recorded as GitHub-computed artifact metadata. The extracted directory was not represented as a reproduction of the server-side archive digest; individual package hashes were independently recomputed instead.

## Exact package ledger

| File | Bytes | SHA-256 | Registry status |
| --- | ---: | --- | --- |
| `CP6.Platform.Abstractions.0.8.0-alpha.1.nupkg` | 8587 | `35a6dd63b72d86f14a76acce127f789f8614512d219d7672d249ccbbe73b8676` | Published |
| `CP6.Platform.Abstractions.0.8.0-alpha.1.snupkg` | 9680 | `800df2c0a1b0a992acaaf706041e9348d45d2775dafbcb636e20daeb4b9aac49` | Artifact only |
| `CP6.Platform.AspNetCore.0.8.0-alpha.1.nupkg` | 38947 | `7dd69b7b3b7a39d9329d558cf6158720aa9480796f74fc533ab01224482f5db1` | Published |
| `CP6.Platform.AspNetCore.0.8.0-alpha.1.snupkg` | 26424 | `8c1163ce663e3812c40d4abb6158d7af8b7e71442d1a4cf45f89e00a6bf60eaa` | Artifact only |
| `CP6.Platform.Contracts.0.8.0-alpha.1.nupkg` | 31354 | `8390b84f9dc158f20ebae6d0e7f1ca3a0a6817df24cad87e927b639e6deef29d` | Published |
| `CP6.Platform.Contracts.0.8.0-alpha.1.snupkg` | 24275 | `6a6e6532301add9ec152d564d604f5207236613a8bca3c9559475277f1f552da` | Artifact only |
| `CP6.Platform.EntityFramework.0.8.0-alpha.1.nupkg` | 44131 | `b240708a82298998a599cfaf8c4a018c21a0054471269c8e6ab3b801dcd41a91` | Published |
| `CP6.Platform.EntityFramework.0.8.0-alpha.1.snupkg` | 16822 | `814dd1f16098e2ecbc7df94c91ec05c297c3b4c2a6bc11a0884037689ee64608` | Artifact only |
| `CP6.Platform.Messaging.0.8.0-alpha.1.nupkg` | 44474 | `593c58ceb2712cd440d181a526e956a1e65ec387a61775362e53d49f2ceed396` | Published |
| `CP6.Platform.Messaging.0.8.0-alpha.1.snupkg` | 24787 | `c1f12f51ae19af0ceafe389501533aa3ff262411fddee3dd00e430f63afe6bd0` | Artifact only |

The downloaded `sha256.json` contained exactly these ten filenames and hashes. All ten files were non-empty, and every SHA-256 matched an independent `Get-FileHash` calculation.

## Retained gate and content evidence

- Artifact roots were exactly `release`, `verify`, `p05-integration`, and `p06-sql-integration` with 71 files in total.
- P05 `result.json` reported `Passed` with Dapr `1.18.2`, Kafka `4.3.1`, valid service invocation and a contract-valid partitioned event. Its `docker-compose.log` was non-empty.
- P06 `result.json` reported `Passed` with SQL Server `2022-CU26-ubuntu-22.04` and runtime image digest `sha256:ba4c8329f48fb8f02e1416be6a930ebfd71268caee78aa985f3af4315e457c89`. Its `sql-server.log` was non-empty and the result retained all seven approved transactional checks.
- The `verify` root retained 56 non-empty files for Format, Build, Unit, Integration, E2E, Contract, Security, two independent packs, summaries, logs, JUnit results, and package entry hashes.
- Independent archive inspection proved one non-empty `lib/net8.0` runtime assembly per ordinary package, P08 assets only in Contracts, P04 event assets only in Messaging, and no Testing assets, machine paths, or unsafe text content.

## S02 boundary

The retained alpha.1 evidence proves its immutable historical publication only; CRM must not consume it as the P08 candidate. Alpha.2 is the sole forward consumer candidate. CRM fixed-version consumption, machine locator reconciliation, public project-memory synchronization, and the final evidence audit remain S03-S06. P08 is not `Frozen / Consumable` at this stage.

## Stable contract identifiers

Operations are `cp6.http.outbound`, `cp6.messaging.dapr.invoke`, `cp6.messaging.publish`, `cp6.messaging.consume`, `cp6.outbox.dispatch`, and `cp6.inbox.process`. Stable HTTP failures are `OperationNotAllowed`, `IdempotencyRequired`, `AttemptTimeout`, `TotalTimeout`, and `CircuitOpen`. The SLO schema ID is `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json`.
