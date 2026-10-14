# Issue 66 — committed Blocked epoch recovery

Fixes `GTX537/CP6.Platform#66` within the WP02-08 library correction window. Branch `codex/bug-66-outbox-blocked-20261014` was created from freshly fetched `origin/main` `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`, then explicitly merged candidate `3e4c078434ff4ae066f20d83e134fb268663700c` as its prerequisite. The original worker worktree was not edited.

`AdvanceEpochV2` now persists the Blocked transition through the existing `SaveSwitch` path. Route state, exact mutation input/output and audit commit together. An identical mutation replay returns the original result without another state/audit write, even after handover authority later resolves; a changed tuple conflicts. Advancing after that resolved original result requires a new mutation and the returned control version.

The new real-PostgreSQL regression first failed against the unchanged candidate: a real COMMIT followed by a suppressed application acknowledgement returned `Conflict` on exact replay instead of the original `PendingCommit`/control version. See `red.log`, `red.trx`, `red-inputs.json`. After the one-path correction, `green.trx` passed 5/5 (new regression, existing blocked rollback, drain behavior and both epoch/completion race orderings), zero skipped. No remote Actions was used.

Each run manifest captures actual pre-run source bytes, input Git HEAD plus dirty status, the command, post-run dependency graphs and loaded-output artifact hashes. These are builds from the recorded working input, not a claim that the later fix commit was rebuilt. The original 153-case evidence remains at its original source/assembly identities; unrelated successes are reused only for their unchanged scope.

`resource-restart.json` records the verified existing task-only owner/system/database/OID/runtime role before testing. The resource is reserved by this correction batch for Issue67 next; final verified stop evidence is recorded in the combined correction delivery. No other database or port was used. Main/CRM Owner/broker/business acceptance remains NOT_RUN.

The original task report's unavailable-project-assets wording was narrowed because its raw failure log is absent; no missing log was fabricated.
