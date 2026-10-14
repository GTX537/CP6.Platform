# WP02-08 coordinated correction delivery — Issues 66 and 67

Both concentrated-review defects have minimal fixes and real PostgreSQL RED/GREEN evidence. The combined branch is ready for COORD's independent focused review; this is not a remote-main completion, Issue closure or Owner adoption claim.

| Input / correction | Identity |
|---|---|
| Fresh confirmed main | `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` |
| Original candidate | `3e4c078434ff4ae066f20d83e134fb268663700c` |
| Issue 66 fix | `6e580bb4f77d943dcdaeeafd3450891f791aef9e`, `codex/bug-66-outbox-blocked-20261014` |
| Issue 67 fix | `f17b8669b9dde1826bb231358a0106d16232640b`, `codex/bug-67-outbox-budget-20261014` |
| Combined tested source | `17886458022bcbbb49d63890e52eccd897ac7256`, `codex/wp02-08-corrected-20261014` |

Both fix branches were separately created from the freshly fetched main and explicitly merged the original candidate as a prerequisite. Their worktrees are respectively `D:/CP6.Platform-worktrees/bug-66-outbox-blocked-20261014` and `D:/CP6.Platform-worktrees/bug-67-outbox-budget-20261014`. The combined worktree is `D:/CP6.Platform-worktrees/wp02-08-corrected-20261014`; it retains both independent fix commits and merged their test additions without conflict. The original worker worktree remains clean at the original candidate.

Issue 66 changes only the Blocked branch to use the existing immutable switch-receipt path. Its exact mutation input/result is now atomically retained with the root state and audit, so lost-acknowledgement recovery and exact replay work without additional writes. Changed input conflicts; resolved handover does not silently reinterpret the original mutation. See [issue66/REPORT.md](issue66/REPORT.md).

Issue 67 consumes the approved budget from before send-admission begins, using monotonic elapsed time. The DB clock remains authoritative; the earlier timer conservatively reduces the window proven by its lease gate. COMMIT acknowledgement/disposal/decoding consume that window; exhausted budget and pre-signalled cancellation prevent publisher entry. A valid delay gives transport only the remaining timeout with no route locks. Original send-intent/mutation identity is preserved. See [issue67/REPORT.md](issue67/REPORT.md).

## Verification and preserved failures

| Run | Actual outcome |
|---|---|
| Issue 66 RED | 1 failed: committed Blocked replay returned Conflict instead of the original result |
| Issue 66 GREEN | 5/5 passed: new recovery regression plus blocked rollback, drain and both epoch/completion races |
| Issue 67 RED | 2 failed / 1 passed: expired send still invoked publisher; delayed COMMIT restarted about 5.011 seconds of transport budget |
| Issue 67 cancellation RED | 1 failed: cancellation after actual COMMIT still invoked publisher |
| Issue 67 GREEN | 7/7 passed: four new cases, prior ambiguity cases and database-clock expiry |
| Issue 67 probe assertion follow-up | 1/1 passed after making probe success explicit outside dispatcher exception handling |
| Combined source/DLL smoke | 3/3 passed: blocked recovery, reduced timeout with no held route lock, short-delay successful publish |

There are 12 distinct successful correction/affected cases, of which five are new. All runs have zero skipped cases. The seven TRX, raw logs and run manifests are retained and rehashed. A single correction-batch self-review inspected the complete production diff and test/failure evidence; no additional defect was found. COORD's independent focused review remains pending.

The combined smoke ran at clean source `17886458022bcbbb49d63890e52eccd897ac7256` using SDK 8.0.424. Its 142 source identities, four dependency graphs and 39 artifact hashes were rechecked against current bytes without mismatches. The loaded EF DLL SHA256 is `6c417c4f832f0b707eba03585c68917c4f03bb3cf7f82fd7baf8b556171bf304`; test DLL SHA256 is `ce85532011f61d5514720aed04d96e552ebedfc5d18e1e230856f7cb592eacc1`. [summary.json](summary.json) records individual branch heads, complete test outcomes and final identity details. The run helper is retained as [run-correction-check.py](run-correction-check.py).

Each RED/GREEN manifest identifies its actual working input and dirty state; later commits are not presented as having been rebuilt. No product source changed after the combined smoke. Subsequent evidence-only commit(s) therefore reuse this smoke. The original 153 successful cases remain at their original source/assembly identities; relevant corrected behavior was retested as above, while unrelated architecture/unit/PG results are reused only for unchanged scope. Neither the original full suite nor the whole repository was rerun.

The earlier statement about retaining an unavailable-project-assets failure log was corrected because that raw log is absent. No evidence was invented. The only correction setup command error was `git merge --stat=0`, rejected before mutation and retried with the supported `--no-stat`; it was not a test result.

## Resource shutdown and retained boundaries

The exclusively assigned original task cluster was restarted only after canonical-path, stopped owner marker, on-disk system ID and free 55428 checks. Runtime verification then confirmed owner `dcdfb04dda48edc0d52d638b5e7bd2f0`, system ID `7696392773160066692`, database `cp6_wp0208_20261014_dcdfb04d` / OID16385, the distinct restricted runtime principal and zero foreign clients. No credentials are recorded in this evidence.

After testing, the same identities and zero foreign sessions were checked again and `pg_resource.py` performed smart shutdown. [integrated/resource-stop.json](integrated/resource-stop.json) confirms no postmaster PID and no 55428 listener; the socket probe and independent PowerShell no-match check agree. Data and logs remain on disk. No 5432, business database, other owner resource or session termination was used.

No source outside the task window, root build/dependency configuration, shared contract, InboxV1, workflow, Main roadmap or shared status file changed. No remote writes, PR/Issue mutation, Actions run/cancellation, package publication or deployment occurred. Main/CRM real Owner/writer/host adoption, broker/network end-to-end, migrations/cutover and all 60 original business AC remain UNPROVEN/NOT_RUN. COORD owns independent review, remote integration and later Issue closure.
