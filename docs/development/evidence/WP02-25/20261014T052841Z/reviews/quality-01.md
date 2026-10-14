# Whole-task code quality review 01 — CHANGES_REQUIRED

Reviewer `/root/wp0225_quality_review`; read-only review of actual task diff after spec corrections. No reviewer build/test/edit/commit/push. Three substantive P2 findings:

1. `TelemetryPolicyV1.cs:48`: null tag key with string value reaches dictionary lookup and throws. The synchronous MeterListener Bridge does not isolate it, so business Counter.Add can throw. Reject null/oversized keys and isolate callback failures; prove actual Meter/request behavior remains unchanged and unsafe data never reaches output.
2. `ObservabilityRuntimeV1.cs:140`: Log checks disposed then calls safeLogs.CreateLogger. Concurrent disposal can close the factory between those operations and return ObjectDisposedException to the caller. Cache the logger and make shutdown failures safe drops; exercise concurrent logging/disposal.
3. `HealthAdapterV1.cs:118`: UTC cache age accepts negative elapsed values. Wall-clock rollback can retain old Healthy beyond freshness. Use monotonic elapsed time and a controlled-clock regression; do not change the system clock.

Parent accepted this one correction batch. Only affected code paths and tests require follow-up review. Final formal gates have not run. Original failures and this review remain preserved; resolution is recorded separately.
