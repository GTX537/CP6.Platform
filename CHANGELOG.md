# Changelog

All notable changes to CP6.Platform are documented here.

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
