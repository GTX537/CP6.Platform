# Changelog

All notable changes to CP6.Platform are documented here.

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
