# Release identity or SLO evidence drift

P08 status: S00-S04 complete; S05-S06 pending. Current decision: `Published / Consumer Candidate`.

Runbook ID: `CP6-P08-RELEASE-001`

## Symptoms

`/health/release` returns 503, Candidate identity validation fails, package/artifact/contract digests disagree, SLO parsing rejects evidence, or evaluation returns `Indeterminate` because definitions, coverage, samples, exclusions, or release bindings drift.

## Impact

The running artifact cannot be proven to match its declared release or the SLO evidence cannot support a reliable result. It must not be treated as a verified candidate or production SLO pass.

## Stable query ID

`CP6-P08-RELEASE-001` reconciles safe release identity and SLO schema `https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json` by immutable digest and UTC window.

## Safe diagnosis

Compare canonical service/version, 40-character lowercase Git SHA, `sha256:` artifact and contract digests, SLI definition digest, query-definition digest, evidence-artifact digest, coverage, sample count, completeness, exclusions verification, and computed result. Use synthetic examples with `productionSloClaimed=false` during S01.

## Containment

Block promotion of the mismatched candidate and classify incomplete evidence as `Indeterminate`. Do not edit evidence in place, substitute a mutable package, suppress digest checks, or reinterpret NonCandidate evidence as Pass.

## Recovery

Regenerate evidence from the verified immutable inputs and full UTC window, then parse and evaluate it again. If release identity is wrong, produce a new reviewed candidate through the authorized release stage.

## Validation

Confirm Candidate fields are complete, release/artifact bindings agree, sources and definitions have valid matching digests, coverage is complete, exclusions are verified, and the serialized result equals the fail-closed evaluator. NonCandidate, partial, missing, drifted, or sample-free fixtures must not Pass.

## Escalation

Escalate through the approved release-evidence process when immutable inputs disagree or evidence provenance cannot be reconstructed. Provide digests, schema version, UTC window, stable result, and validation output only.

## Evidence retention

Retain the immutable evidence file, its hash, query/definition hashes, release identity, parser/evaluator output, and review decision according to evidence policy. Do not retain environment addresses or credentials.
