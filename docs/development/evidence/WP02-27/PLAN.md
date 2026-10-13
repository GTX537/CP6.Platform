# WP02-27 local implementation plan

Confirmed Platform baseline: `9259a45d39bae7e98ca804a74778a588c1ad4619`.
Main assignment: `47c9b12c796620effe41c62d16992ccd629ee949`, READY/CONFIRMED, B.
Original RELEASE design: SHA256 `9246ef6b40f500da22c570bf3c31a51701442bbbed50250d9f259d12cb1bbdbf`.

1. Pin the original design, admission anchors and actual seven 0.10.2 archives. Read current release governance; distinguish source delivery from published capabilities. Publish an actual START through the assigned Handoff branch.
2. TDD a closed versioned CapabilitiesV1 matrix, exact caller-approved immutable identity, artifact-byte verification, fail-closed compatibility matching and deterministic consumer lock. Keep existing formal v1 protocol, versions and trust semantics unchanged. Add only task-owned assets to Release packing.
3. Exercise malformed/duplicate/canonical JSON, ID/version/source/feed/hash/set mismatches, unknown capability/profile/environment axes, Current/Historical/Revoked trust and evidence gaps. Preserve RED/failed results, then run the selected Release regression and Architecture gate once at task completion.
4. Run the existing unmodified formal verifier against all seven cached immutable archives with the real pinned policy, certificate, current evaluation time and online revocation. Retain any failure/UNKNOWN; never substitute an offline result or synthetic signer. Add task-local archive/timestamp/identity negatives without altering original artifacts.
5. Build uniquely versioned unsigned local candidate artifacts only where needed to prove included APIs and packed schema bytes. Bind actual source commit, working source/assembly/archive hashes and attempt. Mark NOT_PUBLISHED and NOT_FORMALLY_TRUSTED; no release version slot, signing-root change or feed push.
6. Map all original 32 acceptance criteria to bounded library assertions or the responsible remaining publication/consumer/host/runtime/business qualification. WP02-02 stays blocked on a separately trusted consumable artifact.
7. Perform one task-level specification and quality review, fix substantive issues with affected checks, inspect complete diff, workflow event/path filters and actual task run queue. Commit/push only task files and publish fixed-SHA DELIVERY. COORD owns shared state and protected integration; verify remote main inclusion before DONE.

Independent core implementation uses subagent-driven-development/TDD; root owns read-only archive verification, runners, documentation and communication. Builds are serialized. No database/resource window, Actions execution, shared configuration/trust mutation, deployment or real-host adoption is authorized.
