# Issue #69 correction

Live and identity handlers now send Cache-Control:no-store, matching startup/ready. All four operational responses preserve their JSON wire shape, authorization and statuses; unhealthy startup/ready responses remain 503 with no-store.

SDK 8.0.424 / runtime 8.0.30 RED: live and identity failed the actual Kestrel HTTP header assertion; four startup/ready positive/negative controls passed (2 failed, 4 passed, exit 1). GREEN: all 6 passed (exit 0). TRX, console, exact commands and source/dependency/binary before/after snapshots are retained in red-01 and green-01. Authorization-denial middleware output is unchanged; this correction covers the four mapped operational response handlers.

Self-review found only two handler-body and documentation changes plus the real HTTP regression. Diff checks pass. COORD will aggregate #68, run the once-per-batch affected regression and arrange independent focused review. No push/PR/Actions/database/shared state/production action. Issue closure requires COORD remote-main verification.
