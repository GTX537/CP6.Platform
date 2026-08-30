# Trace propagation or exporter incident

P08 status: S00-S02 complete; S03-S06 pending.

Runbook ID: `CP6-P08-TRACE-001`

## Symptoms

Expected server/client/server spans no longer share one W3C trace, `cp6.messaging.trace_context.rejected` rises, or telemetry export stops while application requests continue.

## Impact

Diagnostic continuity is reduced. Business processing must remain governed by HTTP and P04-P06 contracts; telemetry loss must not change the response, authorize identity, or suppress a valid operation.

## Stable query ID

`CP6-P08-TRACE-001` selects the six stable operations: `cp6.http.outbound`, `cp6.messaging.dapr.invoke`, `cp6.messaging.publish`, `cp6.messaging.consume`, `cp6.outbox.dispatch`, and `cp6.inbox.process`.

## Safe diagnosis

Confirm service/version/environment/region resources, compare span parent relationships, and count rejection by stable error code. Check bounded exporter processor health through host-approved controls. Do not copy raw headers, payloads, identity fields, correlation IDs, event IDs, trace IDs, endpoint addresses, or exception text into incident evidence.

## Containment

Keep business traffic on the existing fail-closed path. Reduce optional diagnostic sampling only through the host's approved runtime configuration. Do not bypass validation, enable baggage, or add high-cardinality labels.

## Recovery

Restore the host-owned exporter or processor configuration, then allow new requests/messages to establish fresh W3C context. Invalid remote context remains rejected; do not replay it as trusted context.

## Validation

Run a synthetic A-to-B request and verify one trace with distinct A-server, A-client, and B-server spans. Confirm correlation remains independent, baggage is absent, malformed context creates a fresh root, and exporter failure cannot change the synthetic response.

## Escalation

Escalate through the consumer's approved incident process when export remains unavailable, rejection rate changes unexpectedly, or business behavior differs with export enabled versus disabled. Include only stable IDs and aggregate counts.

## Evidence retention

Retain redacted query output, release identity, UTC window, query-definition digest, evidence-artifact digest, and validation result under the consumer's evidence policy. Do not retain raw telemetry envelopes.
