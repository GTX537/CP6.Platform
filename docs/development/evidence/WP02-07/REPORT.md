# WP02-07 delivery report — Platform library candidate

**Candidate implementation validated; awaiting COORD serial integration.** This is a library/isolated PostgreSQL delivery, not Main/CRM adoption, original60AC business acceptance, formal package publication or deployment.

## Source and scope

- Task branch `codex/wp02-07-caller-inbox-20261013`, worktree `D:/CP6.Platform-worktrees/wp02-07-20261013`.
- Confirmed Platform baseline `9259a45d39bae7e98ca804a74778a588c1ad4619`; implementation `ed6ee2972d16ea747e706be3dce258dc299cd1b8`; test assertion fix `57a94a4f93338558a6499989975b3796ba5b4f72`; Windows task-launcher fix `9bc656acdb67d844218f6788937284b897da45d7`. Final candidate additionally carries this evidence; Handoff DELIVERY pins its complete SHA.
- Main original input `a1824fec3d4824d45e27693f2e715b311ee5f665`; authorized dispatch `47c9b12c796620effe41c62d16992ccd629ee949`; CRM reference `1e4eea61c31d7547c3b3495d392eefbed99b7339` not modified. S01-09 and WP02-01 delivered on the confirmed Platform baseline.
- Original PLT-DEL v0.2.1 SHA256 `642a2fbc35d17fbfaa0bcc51617e24654cbe193e0f907674a3e6f4c41c47eb84`; S6 v0.2 `d9b57a039e18a689615cfb48e181bb248dedecf957d8bdd7dd72d85a53a6dccc`; raw source hash verification and fixed Git input identities in [source-inputs.json](source-inputs.json).
- Only new InboxV1 code, task-private unit/PG tests, integration documentation and evidence changed. Legacy processor/models/enums/Outbox/retention, Contracts/Abstractions/Messaging, root projects/dependencies/solution, workflows, Release and ArchitectureTests are unchanged. Four shared Main state files and roadmap belong to COORD.

## Delivered behavior

Actual caller DbContext/native transaction/connection and trusted tenant are required. Serializable write mode, native backend/database/principal/xid and held PostgreSQL transaction advisory locks are checked through the operation. Library never starts/commits a transaction or records an exception in a separate transaction. Owner delegates its existing exact seven-stage lock protocol; unsupported/pre-held protocols stay closed.

Content admission precedes duplicate/old/equal decisions and all retry re-entry. Original raw envelope bytes, root data bytes and Owner canonical digest domains remain distinct. Transport binding includes the original operation; transport conflict wins, known historical same-version conflict cannot be hidden by a newer checkpoint. Foreign-tenant message collision records a technical conflict without invoking incoming-selected Owner mutation.

Business/journal/Published/projection/observation writes plus Inbox/checkpoint/immutable result copy/outcome participate in one caller transaction. Conflict records do not overwrite original results. Only a persisted exact receipt read under current read policy yields a delivery outcome; pending has no transport success mapping. Original-key-only historical recovery has a separate result type with no transport acknowledgment. Read does not require a new-write grant. ERP retains its own CommandInbox/journal authority.

Four opt-in technical tables use independent names/schema, unique transport/original-operation/checkpoint keys, restrictive result linkage and real PostgreSQL `uint` xmin concurrency. No legacy technical-retention query addresses these new entities. Full hold/retention/lease/requeue and real host migrations remain separate.

## Local verification and reuse

Environment: Windows x64, .NET SDK8.0.424/runtime8.0.30, EF8.0.30, Npgsql8.0.8, PostgreSQL EF provider8.0.11, PostgreSQL18.6. Debug/net8.0, root audit/warnings policy retained; no dependency downgrades or disabled warnings. All tests discovered and executed, zero skips in passing runs.

| Evidence | Result | Exact scope |
|---|---|---|
| `inbox-unit-final.trx` | 18/18 | Seven-decision classifier, exact bytes/digest separation, duplicate JSON rejection, wrong source/tenant, pending and original-key result surface, immutable operation binding |
| `inbox-postgresql-reviewed.trx` | 20/20 | Actual nine-table commit/full rollback; conflict-health rollback; missing/different/ended handles; tenant collision; real seven locks; same-key23505 and different-version40001; whole-original-key retry; same-data/different-transport; unknown commit/read rights; real xmin; two actual shared contexts; readonly mode |
| `inbox-architecture.trx` | 98/98 | Required repository architecture gate |
| `inbox-review-regression.trx` | 4/4 | After P3 review fix, overwrite the actual caller byte array; all changed theory cases pass |
| `inbox-resource-reproduction.trx` | 2/2 | Corrected helper's fresh cluster/bootstrap/runtime identity + commit visibility and shared-context smoke, reusing unchanged PG test assembly |

The accepted base library/PG tests bind to implementation `ed6ee29…`; subsequent commits changed one unit assertion and the private Python launcher, not production code. Reused 18-unit/20-PG/98-architecture successes keep their actual original logs/times; they are not relabeled as new executions. The four changed unit cases and two provisioning smoke cases are separately recorded. [validation-inputs-before-review-fix.json](validation-inputs-before-review-fix.json) preserves the earlier source/build capture; [validation-inputs.json](validation-inputs.json) records final working/Git source pairs, dependency graph versions/hashes and artifact hashes. [test-runs.json](test-runs.json) lists every retained TRX with actual counts/outcomes/times.

The full source manifest covers255 source/config/contract files,9 resolved dependency graphs and153 build artifacts. The table reports **136 distinct test cases**, plus4+2 repeated targeted cases; repeated passes are not additional business AC. Real SQLSTATE23505 and40001 appear in PG TRX output, along with native database/principal/backend/xid diagnostic identities. Commit confirmation loss is an explicit application fault injection after a real commit, not a fabricated physical network outage.

## Failure and resource history

Retain all failing TRX/logs: absent API expected RED (1), first PG setup OID JSON string/number mismatch (12 fixture failures, no acceptance), cross-tenant Owner callback regression (1 of13 failed), immutable operation-binding expected RED (1), key-only result API expected RED (1). All were fixed or fulfilled; earlier uncommitted failure logs are diagnostic evidence, not accepted source-pinned successes. Intermediate passing runs also remain without inflating final counts.

Only reserved127.0.0.1:55427 and `D:/CP6-local-test/WP02-07/<owner32>/` resources were used. Three unique owner resources are listed in [resources/resources.json](resources/resources.json), with raw non-secret ownership/init/server/smart-stop logs and hashes. Credentials were newly generated, SCRAM, restricted to private local ACLs, and **not copied or committed**. Runtime roles are NOSUPERUSER/NOCREATEDB/NOCREATEROLE/NOINHERIT/NOBYPASSRLS with fixture DML and narrowly scoped identity-read function only. Every fixture reset verifies owner/marker/absolute data path/port/system identifier/DB OID/owner/runtime principal and refuses other client sessions. Fixture schema alone is recreated; existing host databases are never connected.

Initial Windows captured startup pipes did not return despite server readiness. Cancelling the first setup command also interrupted its just-created task server, before task application database creation; that exact owned cluster was restarted with Hidden launch and used for tests. A second owned resource reproduced the launcher wait and was resumed from its pre-start manifest; it ran no business scenarios and was smart-stopped before cancelling its waiting helper. The final helper uses a detached Hidden launcher with file stdio and actual identity readiness, and fresh creation returned successfully. Its two smoke cases passed. All three owned servers subsequently passed identity/zero-other-session checks and smart shutdown; data/logs retained, no recursive filesystem deletion, no existing5432 service or database touched. Port55427 has no listener at delivery.

## Review, acceptance and integration

[REVIEW.md](REVIEW.md): one independent concentrated review, no blocking findings. P3 test recommendation fixed and narrowly retested. Helper-only startup fix separately reproduced; no full-review loop.

[acceptance-map.json](acceptance-map.json) preserves all60 complete original assertions, subassertions and identities. It separates tested local subassertions, static compatibility evidence and outside-task obligations. **All original business acceptance remains NOT_RUN and real Owner adoption UNPROVEN.** In particular, full registration/revocation/writer coverage, current final business fence, reconciliation authority selection/CAS, ERP/CRM integration, raw replay source/PII policy, migration/retention/lease/cutover/production behavior are not established here.

No GitHub Actions triggered, rerun or cancelled; no workflow or branch-protection change. Before task branch publication, candidate and protected-main workflow files/events/path filters/dependencies are checked; only the existing RFC3161 narrow PR filter is automatic, and neither listed path is in this task. No package, tag, registry candidate, host deployment or production migration. Candidate transport and final raw Handoff identities are supplied by DELIVERY.

COORD handoff: review the exact candidate and local evidence, perform the serial Platform PR/main merge and required integration smoke/reuse, verify remote main contains the task, then update Main roadmap/generated views and PROJECT_STATE/05-Completed/06-Todo/CHANGELOG-AI in the same task integration window. Suggested state wording: “WP02-07 library caller-transaction Inbox/original-result capability verified on isolated PostgreSQL; Main/CRM Owner adoption and formal package release remain separate.” Do not mark DONE merely from this candidate report or Handoff ACK.
