# WP02-08 delivery report

**Verified Platform library candidate; COORD integration pending.** Implementation source commit: `495a41f7a238e6af5d19e4daa1e3880c3ff33f4c`. Later commits add evidence only. The fixed final candidate/remote branch identity is supplied in this task's Handoff DELIVERY, not inferred from this report. No claim that remote main already contains the task.

Inputs: Main `1ab2d031e3c501662ecb89a33839fac1cba55d33`; Platform baseline `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0`; Handoff wave6 `ad255c21810a101662fd3e380992a03fde71407e`. START was published at `df6057f159537dd39b99904b139c68147b1e06c5`, path `handoffs/CP6-DEV-20261012/tasks/WP02-08/messages/20261014T053047Z-c-start/MESSAGE.md`; remote/Git/local raw bytes were compared. WORKTREE `D:/CP6.Platform-worktrees/wp02-08-20261013`, branch `codex/wp02-08-outbox-20261013`. Fresh prepublication fetch still confirmed Platform main at the baseline. WP02-07 is an already merged predecessor; its closed window and files remain unchanged.

## Delivered behavior

- Opt-in `cp6-outbox-lease-v2`, version1, with caller-owned claim/complete/renew/drain/advance/retention seams and current-read original receipt recovery. Native queue identity, exact bytes/hash and global MessageId uniqueness remain authoritative; companions are technical evidence.
- Actual PostgreSQL transaction/connection/trusted context, Owner authority row locks, shared route root, real post-write xmin tokens, immutable request/mutation tuples and token seals. Prepared and committed ticket types are separate. A serialization abort retries the original tuple in a fresh transaction; an unknown commit never implies rollback.
- Current lease/epoch/native/Binding/hash/tenant CAS and database wall-clock expiry; persisted send intent, bounded publisher timeout, transport/completion split, original mutation recovery and no old MarkFailed fallback.
- Durable drain/epoch handover with no live claim and authoritative unknown resolution; conservative policy/hold/reference/consumer/dedup/archive retention, preserving original receipts/audit. Missing capabilities remain disabled.
- Narrow legacy store/retention exclusions plus explicit Owner-installed PG guards; unbound legacy behavior remains available. No existing dispatcher, InboxV1, legacy entity/type/model, root dependency/project/solution, shared contract or telemetry file was edited.

Detailed API/adoption requirements: [WP02-08-OUTBOX-V2.md](../../../WP02-08-OUTBOX-V2.md). This includes the required complete Owner authority-row inventory; a trusted adapter omitting a real grant/hold dependency is not a valid adoption. Migration asset generation does not install anything into host databases.

## Local verification and review

**153 distinct successful cases:** 15 unit/affected legacy regression, 40 real PostgreSQL cases, 98 architecture checks; zero skipped in the successful runs. These are library/fixture results, not original business AC passes. Exact expiry equality is tested as a strict instant comparison in unit tests; real PG tests exercise database-clock expiration after the boundary, without claiming a test hit the same wall-clock microsecond.

| Run | Result and exact use |
|---|---|
| `tests/pg-agent-review-06/pg-agent-review-06.trx` | Full corrected PG suite:38/38; matching source and loaded-DLL hash snapshot retained. |
| `tests/09-mapping-final.trx` |5/5: two newly added ignored-token cases, existing malformed mapping cases and a real successful claim. PG distinct total is40, not43. |
| `tests/08-expiry-final.trx` |1/1 repeated real PG expiry after equivalent strict helper extraction. |
| `tests/07-unit-final.trx` |15/15, including unsigned xmin codec, exact expiry boundary, no public prepared/committed promotion, diagnostic redaction and related legacy contracts/telemetry. |
| `tests/05-architecture.trx` |98/98. Reused after changes confined to the same permitted source files and task tests; dependencies/shared configuration unchanged. |

Actual concurrency tests observe PostgreSQL advisory waits and test both completion/advance and hold/cleanup orderings. Real23505/40001 aborts retain original keys. Fault injection suppresses application acknowledgement **after a real COMMIT**; it is not a broker or network-partition acceptance. Tests exercise post-write xmin, sealed rows, native/Binding/hash/tenant/epoch drift, renewal stale tickets, DLQ/receipt/audit rollback, read recovery after revoked write permission, absent policy/root/provider/fence, WHEN(FALSE) guard, missing unique index, incorrect and ignored token mappings, all old writer entry points, direct bound native/Binding/DLQ updates, and unbound V1 work. The publisher probe observes no route lock during transport and cannot be invoked again for the same claim after uncertainty.

The one concentrated independent [review](REVIEW.md) found three material issues (stale Serializable authority snapshot, ineffective guard readiness and incomplete mapping readiness); all were fixed and targeted rereview closed them. After the full PG run only the equivalent strict-time helper, additional mandatory-property check, two mapping cases and non-executable EOF whitespace changed. [validation-reuse.json](validation-reuse.json) records exact source/artifact deltas and the applicable later checks; no SHA-only rerun is claimed.

All 11 TRX runs and associated logs are retained, including the initial missing-protocol red test, unavailable project assets, EF1002 build failure, initially unnormalized missing provider, and the initial assertion expecting an unwrapped40001. The latter was a test assertion correction; native SQLSTATE verification remained required. Resource-preparation failures are also retained. No failure was hidden as a skip or remote success. [test-runs.json](test-runs.json) enumerates each original run and outcome.

The manifest contains **278 working/Git source pairs, 9 dependency graphs and 153 compiled artifact hashes**, plus the36 fixed source inputs. Git and working byte identities are separate where line endings differ. Builds actually ran before the source commit, so embedded informational Git identity can be baseline+dirty; the manifest identifies the exact tested inputs and does not relabel those artifacts as rebuilt after commit. SDK8.0.424/runtime8.0.30, EF8.0.30, Npgsql8.0.8/provider8.0.11 and PostgreSQL18.6 are the tested local combination. No other provider/host is certified.

## Resources, publication and remaining responsibilities

Only the authorized fresh task cluster `127.0.0.1:55428`, owner `dcdfb04dda48edc0d52d638b5e7bd2f0`, system ID `7696392773160066692`, database `cp6_wp0208_20261014_dcdfb04d`/OID16385 was used. Ownership, canonical path, marker, runtime role flags and foreign-session absence were checked before fixture DDL and smart shutdown. Runtime was distinct from migration/admin and lacked superuser/CREATEDB/CREATEROLE/INHERIT/BYPASSRLS/schema-CREATE. The cluster is **stopped**, data/logs retained, no files deleted; stop identity and zero listener evidence are included. Old WP0207 credentials,5432, business databases and other task resources were not used.

Eight candidate/protected-main workflow files are unchanged. None triggers on push; the one existing RFC3161 PR event is restricted to two untouched paths. Active Platform runs were empty at intake/prepublication. No Actions was started, rerun, cancelled or reconfigured; no tag, formal package, migration of existing data, deployment or PR was issued by this execution lane. Publication is a normal task-branch push and fixed Handoff DELIVERY. COORD retains serial PR/main integration and Main roadmap/generated/four-state synchronization.

All **60 original DEL AC IDs, source text and raw source hash** are retained in [acceptance-map.json](acceptance-map.json). Each library subassertion is separate from original business acceptance; all60 originals remain NOT_RUN. Main/CRM actual Owner adapters, complete writer/adoption proofs, migrations/old in-flight cutover, trusted package consumption, actual broker/host end-to-end, archive restore and legal policy remain UNPROVEN/NOT_RUN. No schema-per-tenant rollout, SQL Server expansion, business replay, production SLO or network exactly-once claim is made.
