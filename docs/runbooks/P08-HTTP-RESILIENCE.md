# Outbound HTTP resilience incident

P08 status: S00 complete; S01 implementation candidate; S02-S06 pending.

Runbook ID: `CP6-P08-RESILIENCE-001`

## Symptoms

Calls end in `AttemptTimeout`, `TotalTimeout`, or `CircuitOpen`; an invalid method reports `OperationNotAllowed`; an idempotent write reports `IdempotencyRequired`; or approved transient responses consume the configured retry allowance.

## Impact

The dependency call is unavailable or rejected safely. There is no automatic fallback and no synthetic success. Unknown write outcomes must not be replayed outside their declared idempotency contract.

## Stable query ID

`CP6-P08-RESILIENCE-001` selects `cp6.http.outbound` by `cp6.http.operation_kind`, `cp6.outcome`, and `cp6.error.code` only.

## Safe diagnosis

Confirm the named client's operation kind, method class, bounded retry count, attempt/total timeouts, breaker state, and aggregate status category. Do not record full request targets, query strings, request/response bodies, idempotency values, identity headers, or free-form exceptions.

## Containment

For `CircuitOpen`, stop manual retry loops and allow the configured break interval. Preserve caller cancellation. Keep `NonIdempotent` at zero retry and require exactly one valid key for `IdempotentWrite`.

## Recovery

Restore dependency health, then allow the circuit to probe according to its configured clock. Change bounds only through reviewed consumer configuration that remains within the profile limits.

## Validation

Verify reads make at most the initial attempt plus configured retries, non-idempotent writes make one attempt, missing write keys make zero dependency attempts, cancellation ends promptly, no fallback runs, and the circuit returns to normal only after a successful probe.

## Escalation

Escalate through the approved dependency process when the breaker repeatedly reopens, timeout distribution shifts, or safe operation classification is disputed. Share only stable client/operation/error categories and aggregate timing.

## Evidence retention

Retain profile bounds, UTC fault window, stable outcome counts, synthetic probe result, release identity, and evidence digest. Exclude request content and dependency addresses.
