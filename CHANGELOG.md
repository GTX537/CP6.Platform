# Changelog

All notable changes to CP6.Platform are documented here.

## 0.9.0.0 - 2026-08-31

- Complete P09-S01 through P09-S04: strict Profile/Evidence contracts, the independent dependency-free `CP6.Platform.Deployment 0.9.0-alpha.1` package, real Dapr/Kafka Compose rehearsal, offline Kubernetes render/dry-run/policy validation, and exact-main immutable publication.
- Prove exact-SHA service invocation, Pub/Sub, Topic/ACL idempotence, direct-network/AppId/principal/foreign-Topic rejection, canonical content-addressed evidence, and zero container/network/volume/image/temporary-directory residue.
- Add the dedicated `ubuntu-p09-non-production-runtime` job and explicit `-P09Contract` / `-P09Real` verification entries while keeping ordinary Windows/Linux jobs free of Docker rehearsal.
- Publish the P09 package once from merge commit `1c40f21e38929abaaa6006f69ee70d4492890661` in run `33480300468` and independently match Registry version `1194316756` at SHA-256 `e820d1771ed004b4a7089d008eef3bb2aca4fe35e4912d67057840373c4952cb`; retain artifact `9789925866` with its immutable digest. P09-S05 and P09-S06 remain pending.
- Make the required publication gates reproducible from Windows worktrees by normalizing the offline Kubernetes container script to LF and making the P06 SQL runner honor the verified `DOTNET_HOST_PATH`.
- Record P09 as `Published / Consumer Candidate`; CRM fixed-version consumption, public project-memory synchronization, real environment rollout, and final reuse authorization are not claimed.

## 0.8.0.0 - 2026-08-30

- Begin P08-S01 from the approved observability, health, resilience, and SLO evidence design.
- Freeze the exporter-neutral OpenTelemetry and HTTP resilience dependency baseline for `0.8.0-alpha.1` while keeping collectors, dashboards, environment routes, deployment, immutable publication, and CRM consumption outside this implementation stage.
- Add validated immutable release identity and low-cardinality telemetry naming contracts; P08 remains in progress until S01–S06 evidence closes.
- Add safe live/startup/ready/release endpoints, explicit idempotency-aware outbound HTTP resilience, and independent correlation/W3C propagation.
- Instrument Dapr/Kafka and transactional Outbox/Inbox paths without changing P04-P06 validation, topic/key, lease, transaction, idempotency, retention, or DLQ semantics.
- Add a strict Draft 2020-12 SLO evidence contract/evaluator, deterministic repository-only telemetry/fault fixtures, and two-host trace/failure E2E coverage.
- Strengthen package evidence to exactly five runtime plus five symbol packages with reproducible entry hashes, asset ownership, non-empty assemblies, and content-safety enforcement.
- Record S01 as an implementation candidate only; package publication, CRM consumption, locator/public-memory reconciliation, and final freeze remain S02-S06.
- Publish the five immutable `0.8.0-alpha.1` runtime packages from exact Platform main with independently matched package hashes, artifact digest, and complete real Dapr/Kafka and SQL Server evidence; CRM consumption and freeze remain S03-S06.
- Disqualify alpha.1 as the CRM candidate after external black-box testing proved BCL `HttpClient` still forwarded baggage; align BCL and OpenTelemetry propagation to trace-only fields and advance the forward-only replacement to `0.8.0-alpha.2` without overwriting alpha.1.
- Publish the immutable `0.8.0-alpha.2` replacement from exact validated Platform main, independently match all ten package hashes and Registry version identities, and re-close S02 while CRM consumption remains S03.
- Reconcile CRM S03/S04 fixed-version evidence from PR #33/#34 and the PR #35 forward correction, including exact PR/main and SQL artifact identities; keep P08 at `Published / Consumer Candidate` until public S05 synchronization and the Platform S06 final audit.
- Complete public S05 through CP6 PR #72 and exact-main public contract, PRD, Space GA, real SQL, Windows/Web, and Android workflows; issue the S06 `Frozen / Consumable` final decision with an explicit merge-plus-exact-main-four-job effective condition.

## 0.7.0.0 - 2026-08-29

- Add a validated, code-owned YARP route/cluster profile with HTTPS-by-default destinations and mandatory route rate limits.
- Remove external `X-User-*`, `X-Tenant-*`, `X-Organization-*`, `X-CP6-*`, `Forwarded` and client-certificate identity metadata before proxying while preserving Authorization, Cookie and correlation protocols.
- Add per-connection-source fixed-window limiting with safe RFC 9457 `429` responses, plus loopback E2E coverage for route allowlisting, forged headers, direct-backend authentication and destination side-effect suppression.
- Publish five immutable `0.7.0-alpha.1` packages from the exact Platform main SHA and verify their artifact/package digests through CRM PR/main fixed-version consumption and public project-memory gates; P07 is `Frozen / Consumable`.
- Keep P09 NetworkPolicy/port isolation, C01 identity issuance, CRM runtime registration, cloud resources and deployment outside P07-S01.

## 0.6.0.0 - 2026-08-29

- Add caller-owned atomic EF Outbox enqueueing, conditional lease-token dispatch, bounded retry, outbound DLQ and audited requeue while preserving the original event id.
- Add validate-before-database Inbox processing with `(ConsumerName, MessageId)` uniqueness, payload-hash conflict detection, aggregate-version checkpoints, poison-message rollback/DLQ and audited replay.
- Add published Outbox, processed Inbox and dead-letter retention defaults of 7/30/90 days, plus a pinned real SQL Server gate covering rollback, duplicate, ordering, lease-expiry and replay behavior; immutable publication and CRM PR/main fixed-version consumption are verified.

## 0.5.0.0 - 2026-08-28

- Add the P05 Dapr service-invocation and structured CloudEvent Pub/Sub adapters on top of the frozen P04 contract validator; immutable publication and CRM PR/main fixed-version consumption are verified.
- Freeze Kafka topic and tenant/aggregate partition-key conventions, including consumer-side topic/key drift rejection before handlers run.
- Add a real Dapr 1.18.2 and Apache Kafka 4.3.1 container gate; P06 Outbox/Inbox and P09 deployment assets remain out of scope.

## 0.4.0.0 - 2026-08-28

- Add the P04-S01 CloudEvents 1.0 structured JSON profile with required tenant, correlation, causation, aggregate, schema and region extensions.
- Add a content-addressed Draft 2020-12 contract bundle, full positive/negative example matrix, fail-closed validation and same-major compatibility checks.
- Publish the four immutable `0.4.0-alpha.1` packages from exact `main@2c4c601228d81b300659b7773748da2e995ce433`; preserve artifact and package SHA-256 evidence, then close P04 as `Frozen / Consumable` with CRM PR/main fixed-version bundle verification.

## 0.3.0.0 - 2026-08-28

- Add the P03 RS256-only JwtBearer profile with exact issuer/audience validation, required-claim checks, non-empty tenant enforcement and unknown-kid JWKS refresh.
- Add the safe RFC 9457 problem-definition/writer contract and correlation middleware without exception, token, request payload or PII disclosure.
- Publish the three immutable `0.3.0-alpha.1` packages and close them as `Frozen / Consumable` with negative-token, rotation, Problem Details, correlation, architecture, package-digest and CRM PR/main consumer evidence.

## 0.2.0.0 - 2026-08-28

- Add the exact read-only P02 request-context contract, immutable snapshot and validated implementation without tenant or user defaults.
- Add the ASP.NET Core trusted resolver boundary and fail-closed middleware with unit and integration evidence.
- Add a manual, main-commit-pinned GitHub Packages workflow for the three non-empty `0.2.0-alpha.1` packages; immutable version collisions fail instead of being skipped.

## 0.1.0.1 - 2026-08-27

- Align the authoritative P02–P10 roadmap with the CP6 executable specification and close the numbering drift found during CRM pre-landing review.
- Record P01 as merged, main-CI verified, and `Frozen / Producer Ready`; keep every P02+ runtime capability explicitly absent.

## 0.1.0.0 - 2026-08-27

- Establish the P01 repository, six-package dependency boundary, deterministic package metadata, verification contract, architecture tests, and Windows/Linux CI.
- Record private GitHub Packages consumption, source mapping, deferred formal signing, and the no-empty-package rule.
