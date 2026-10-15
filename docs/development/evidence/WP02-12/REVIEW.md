# WP02-12 task review

One integrated read-only spec/security/quality review was performed by the independent `wp0212_review` agent against the complete task working diff and wave7 card. Scope included immutable route/producer authority, legacy opt-in compatibility, byte/digest ownership, quarantine durability, corrected Outbox bridge, Kafka/Dapr/PG fixture provenance and evidence limitations. The reviewer started no services, tests or credential access. Parent inspected the complete staged scope before the audit commit; no second full review was started for each edit.

## Material finding and resolution

**P2: repeated invalid message at a different Kafka offset could retry forever.** Durable quarantine identity originally included profile/route/policy/messageId/raw hash, while exact readback also compared native facts containing the offset. The second delivery selected the first row and failed equality, so it could not DROP after a distinct committed observation.

Fixed by adding `nativeReference` to the readback contract and fixture row/query identity. Existing rows were retained, filling the new fixture column from stored facts before expanding its key; no production schema or migration changed. Original message ID, raw hash and bytes remain unchanged. Exact facts comparisons still apply within each delivery reference.

Parent also corrected authenticated wrong-key poison handling: a bounded actual wrong key may receive a receive-only quarantine capability. It remains `IsQualified=false`, cannot publish or reach a business handler, and retains the actual key. Wrong producer/topic authentication still fails without a capability.

Evidence:

- Retained RED: `tests/library/20261015T004811372Z/red-native-reference-readback.trx` and `20261015T004841988Z/red-native-key-quarantine.trx`.
- Targeted GREEN: 27/27 in `tests/library/20261015T004920812Z/green-native-reference-key-quarantine.trx`.
- Native regression: 7/7 in `runtime/targeted-review-regression.json`; offsets 5 and 7 each retain identical original body/id/hash in separate committed rows and each has a real Dapr DROP observation.
- Wrong key: committed quarantine row preserves `wrong-key`; no separate exported DROP observation is claimed for that case.

The same reviewer performed a focused followup on these corrections and closed P2 with no introduced material blocker. It also confirmed `LoadForTransport` hashes and parses the same schema byte array, avoiding a double-read identity race. Its remaining minor evidence wording comment was resolved by narrowing `runtime/reuse-boundaries.json` to durable isolation for the wrong-key case; no runtime result was rewritten.

## Final review boundaries

Trusted Host seams require honest native authentication and an independent committed store; unit fakes do not prove those properties. The native fixture supplies bounded local proof only. The observation receiver is not a business Owner, the ACK-suppression case is application injection, and the console observer is limited to its controlled corpus. Production TLS, lifecycle, retention, C02 adoption, business AC and SLO remain outside this window. No release-level or full-repository test claim is made.
