# Issue #68 correction

Freshness now checks the original observer-owned monotonic timestamp and the UTC observation metadata. Either expired bound permanently closes this evaluator; frozen/rolled-back UTC and a separately supplied evaluator clock cannot extend the byte-load budget. Clock errors remain safe Unproven. Public DTO/wire signatures and trusted-package/SLO/Owner boundaries are unchanged.

SDK 8.0.424 / runtime 8.0.30 RED: 7 actual assertions failed (exit 1), including the real Kestrel route incorrectly returning 200 after UTC rollback. GREEN: all 7 passed (exit 0), including fresh-positive, UTC-only expiry, monotonic-only expiry, separate evaluator clock, throwing clock and real HTTP read/effect/identity behavior. TRX, console, exact commands and source/dependency/binary before/after snapshots are retained in red-01 and green-01.

The runtime captures the monotonic epoch before byte loading; hashing cached bytes never renews it. UTC-only expiry is latched on the evaluator bound to the immutable expected manifest; a consumer cannot treat a fresh evaluator as new observation evidence. No system clock was changed. Controlled clock inputs are local fixtures, not independent production qualification.

Self-review found no extra public dependency/project/contract changes; targeted tests and diff checks pass. COORD will aggregate #69, run the once-per-batch affected regression and arrange independent focused review. Original candidate 255/255 remains historical evidence for its recorded inputs; not newly executed or a blanket corrected-candidate claim. No push/PR/Actions/database/shared state/production action. Issue closure requires COORD remote-main verification.
