# Whole-task specification review 01 — FAIL

Reviewer: `/root/wp0225_spec_review`, independent read-only subagent.
Reviewed baseline `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` versus staged/working WP02-25 implementation after implementation run 22. No build/gate/commit/push or source mutation by reviewer. This is the one initial full-task specification review, not production acceptance.

1. **Route-specific availability missing.** `HealthCheckBindingV1.cs:11` has no check-to-route association; `HealthAdapterV1.cs:86` combines every purpose check and lines 89–92 use a global identity verdict. A write-only dependency failure disables an otherwise healthy read and a module mismatch blocks unrelated effects. Add validated required checks and results per route, use corresponding module evidence, fail closed on missing inventory, retain conservative aggregate and independent read availability. Verify independent read/write dependencies and two effect modules.
2. **Canonical validators accept trailing LF.** New regex `$` anchors in `RuntimeIdentityBindingV1.cs:16/23/80/82`, `HealthCheckBindingV1.cs:42`, and `TelemetryPolicyV1.cs:16` match before a final newline. Read-only .NET regex confirmation accepted a name, digest and module plus LF. Use absolute end anchors or equivalent validation in new contracts, preserving legacy validators. Add LF/CRLF/control character cases for identifiers/digests/modules/source pins/templates.
3. **Observed UTC unchecked.** `OwnedModuleObserverV1.cs:19` stores the supplied clock directly; evaluator checks its own clock but not the observer offset. A nonzero-offset representation of the current instant can produce Match. Reject safely or return Unproven before admitting positive evidence; cover invalid/nonzero-offset/future timestamps.

Owned-byte Match scope and stronger-trust limitations are documented honestly. Unsupported domains, external export, production acceptance and original 32 AC remain appropriately unproven. No forbidden implementation paths changed. Parent final gates and COORD state synchronization are pending by design, not findings.

Disposition: parent accepted all three as within-window blockers and assigned one correction batch to the implementation agent. Final outcome will be recorded in a separate targeted re-review; this original FAIL is preserved.
