# Messaging backlog, retry, or DLQ incident

P08 status: S00-S04 complete; S05-S06 pending. Current decision: `Published / Consumer Candidate`.

Runbook ID: `CP6-P08-MESSAGING-001`

## Symptoms

`cp6.outbox.oldest_available.age`, `cp6.messaging.attempts`, rejection counts, consumer lag, retry, or DLQ disposition rises; publish or consume traces end with a stable failure.

## Impact

Event delivery or processing is delayed or rejected. Existing P04 schema/region, P05 topic/key, and P06 lease/idempotency/order/transaction/DLQ rules remain authoritative.

## Stable query ID

`CP6-P08-MESSAGING-001` selects `cp6.messaging.publish`, `cp6.messaging.consume`, `cp6.outbox.dispatch`, and `cp6.inbox.process` using only operation, outcome, transport, disposition, and stable error dimensions.

## Safe diagnosis

Compare aggregate oldest age, attempt count, disposition, and stable error category. Verify validation ordering, lease ownership, checkpoint, and retry bounds inside protected diagnostics. Do not extract CloudEvent data, event/correlation/trace IDs, tenant/resource IDs, topic names, database detail, or exception text into telemetry evidence.

## Containment

Preserve conditional lease, idempotency, aggregate ordering, transaction rollback, and DLQ audit rules. Pause consumer-owned admission through its approved control if necessary; never bypass the contract validator or mutate a message merely to clear the backlog.

## Recovery

Restore the transport or database prerequisite, let expired leases follow existing rules, and use the audited replay API only for eligible dead letters. Original event identity and business ordering must remain unchanged.

## Validation

Run synthetic publish/consume and transactional fixtures. Confirm duplicate/conflict/order behavior, bounded retries, poison rollback, DLQ-before-replay ordering, retention, and telemetry observation without additional database writes.

## Escalation

Escalate through the approved messaging or data process when oldest age continues to grow, replay is unsafe, or contract validation changes. Provide aggregate counts, stable error/disposition, release identity, and fixture evidence.

## Evidence retention

Retain UTC windows, aggregate lag/age/attempt measurements, stable dispositions, replay audit reference, fixture result, and artifact digest. Exclude message bodies and identifiers.
