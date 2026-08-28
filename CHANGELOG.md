# Changelog

All notable changes to CP6.Platform are documented here.

## 0.4.0.0 - 2026-08-28

- Add the P04-S01 CloudEvents 1.0 structured JSON profile with required tenant, correlation, causation, aggregate, schema and region extensions.
- Add a content-addressed Draft 2020-12 contract bundle, full positive/negative example matrix, fail-closed validation and same-major compatibility checks.
- Keep `0.4.0-alpha.0` unpublished; immutable publication and CRM fixed-version consumption remain separate P04-S02/P04-S03 tasks.

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
