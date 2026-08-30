# Startup or readiness incident

P08 final decision: `Frozen / Consumable`. Effective condition: the S06 change containing this declaration is merged to `main` and its exact-main `platform-validation` passes `ubuntu-latest`, `windows-latest`, `ubuntu-dapr-kafka`, and `ubuntu-sql-server`; until then the PR head is only a final-audit candidate.

Runbook ID: `CP6-P08-HEALTH-001`

## Symptoms

`/health/startup`, `/health/ready`, or `/health/release` returns 503 while `/health/live` remains 200, or a safe component changes from Healthy to Unhealthy.

## Impact

The process is alive but is not safe to start, receive new work, or claim the registered release identity. Existing in-flight semantics remain owned by the service.

## Stable query ID

`CP6-P08-HEALTH-001` groups the four endpoint outcomes and allowlisted component statuses without health detail data.

## Safe diagnosis

Identify which endpoint failed and compare only published component name/status, schema version, UTC observation time, and release identity. Inspect dependency detail inside the consumer's protected diagnostic boundary. Never copy exception text, connection configuration, host names, database names, topic names, or tenant data into the health response or retained query.

## Containment

Stop admitting new work when readiness is unhealthy while keeping liveness independent. Do not remove a required check merely to obtain 200, and do not mark an incomplete release identity as Candidate.

## Recovery

Restore the consumer-owned prerequisite, configuration, or dependency check. If release identity drifted, restore the verified immutable inputs rather than weakening validation.

## Validation

Confirm live does not execute external checks; startup and ready filter only their exact tags; degraded/unhealthy maps to 503; release identity matches registration; every response carries `Cache-Control: no-store` and contains no health data dictionary.

## Escalation

Escalate through the approved service process when a required dependency remains unhealthy, status oscillates, or identity cannot be reconciled. Provide stable component/status pairs and the safe release digest only.

## Evidence retention

Retain UTC status transitions, safe response envelopes, the release artifact digest, validation steps, and final resolution. Exclude protected dependency detail and endpoint addresses.
