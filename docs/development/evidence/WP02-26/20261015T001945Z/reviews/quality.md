# WP02-26 independent code quality review

Verdict: PASS for reviewed implementation. All three blocking findings were corrected and the affected portions re-reviewed; no unresolved code-quality blocker remains. This is one concentrated task review, not repeated full reviews per file.

Scope: complete WP02-26 staged additions and current working changes against b28db763f70ab6f164cb7c87d6ce49a6d97267f1; opt-in exact qualification/capture/fault source, task unit and native PostgreSQL fixture tests, resource/gate/acceptance/workflow helpers, fixed task inputs and safe evidence/delivery report. D:/CP6/AGENTS.md review cadence applies. No tests, builds, network access, PostgreSQL commands, process control, private credential reads or source changes were performed by this reviewer.

## Findings and resolution history

1. Capture byte admission excludes expanded exact numeric storage. Initial EvidenceCase.cs:136 charges original bytes and lexical/metadata only. ExactValue.FromUtf8("1e16384" UTF-8) retains an approximately 6.8 KiB numerator while a 512-byte case budget admits the short token. Include numerator/denominator/coefficient storage before admission; preserve exactness and reject as Incomplete on overflow.
2. Completion can become Complete after inventory/graph enumeration fails. Initial EvidenceCase.cs:211-218 seals before enumerating caller sequences without catch/finally. Record one value x; call Complete with an iterator that yields x then throws; catch and Dispose; Snapshot reports Complete although completion validation aborted. Both expected-key and expected-edge failures must persist an error and finish listener cleanup.
3. Reentrant lifecycle callbacks can publish premature Complete. Both Dispose methods initially set disposed before invoking cleanup; a cleanup callback can retain Snapshot(State=Complete) and then throw, contradicting the final incomplete result. In DeterministicFaultCase, observer runs after incrementing script calls but before terminal work; observer calling Complete(1,0) on a Success script can publish Complete, after which terminal calls become 1 and state remains Complete. Completion must require validation, no active execution, and finished cleanup; callbacks must not expose a successful intermediate state.

No additional blocker found in immutable byte ownership/revision lineage, ratio/weighted-nearest-rank/gauge arithmetic, full trace edges and traversal caps, restricted PostgreSQL identities and session checks, native-versus-injected evidence labeling, or current task file scope.

## Closeout still required, separate from implementation findings

Parent will execute final unit/contract, own-cluster PostgreSQL and architecture gates, verify current source/dependency/artifact identities, generate all 56 original business AC as NOT_RUN with scoped local evidence links, collect clean own-cluster shutdown evidence, and update delivery references. Earlier successful runs retain only their actual historical scope. This review does not establish production SLO, independent acceptance, business Owner adoption, remote-main integration or release readiness.

## Targeted correction verification

1. RESOLVED: ExactValue.cs:18 counts numerator, denominator and optional coefficient using allocation-free GetByteCount; EvidenceCase.cs:139 includes these charges before Admit. Exact values remain unchanged. Large exponent input is rejected as Incomplete within the small budget; a small exact value remains accepted.
2. RESOLVED: EvidenceCase.cs:220-236 surrounds all completion validation with catch/finally, records CompletionValidationFailure, and disposes on either key or graph enumeration failure. completionValidated is set only after those checks finish. Tests require cleanup once and persistent Incomplete.
3. RESOLVED: EvidenceCase.cs:47-49,203-211,223,245,259 and DeterministicFaultCase.cs:46-48,76-85,108,132-145,156 gate Complete on finished validation/cleanup and no active observer/execution. Reentrant cleanup snapshots cannot claim Complete; observer-initiated completion invalidates the case; fault inner work cannot continue after callback closure. Tests cover both capture and fault callbacks.

Read and verified existing implementation evidence (executed by implementer, not reviewer):

- unit-red-numeric-budget-01: numeric exit 1; 2 executed, 1 failed, 1 passed. The expanded numeric reproducer failed and the small valid-value control passed.
- unit-red-completion-01: numeric exit 1; both throwing key/graph enumeration reproducers failed.
- unit-red-cleanup-reentry-01: numeric exit 1; capture and fault premature-cleanup snapshot reproducers failed.
- unit-red-observer-reentry-01: numeric exit 1; capture and fault observer-completion reproducers failed.
- unit-green-final-lifecycle-01: numeric exit 0; 39 executed, 39 passed, zero failed/unexecuted. Its command explicitly filters CaptureTests and FaultTests. All five paths in its source-inputs.json match the reviewed current working byte hashes.

This targeted regression covers the corrections and their directly affected capture/fault suites. It is not the pending complete formal unit/PG/architecture closeout. Unchanged source was not subjected to a second full review.

## Reviewed working byte hashes

Hashes below identify actual working bytes read during this review, including staged files with later working corrections. Source hashes do not imply a commit or remote-main integration. Evidence/report hashes retain their review-time state; the parent may append final closeout separately.

| Path | SHA-256 |
|---|---|

| docs/development/evidence/WP02-26/20261015T001945Z/DELIVERY.md | ff5f844090f2ee49017f2ee34fc985ba094907799555b5dcd2a9d9d950326ce0 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-green-final-lifecycle-01/exit-code.txt | 13bf7b3039c63bf5a50491fa3cfd8eb4e699d1ba1436315aef9cbe5711530354 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-green-final-lifecycle-01/source-inputs.json | b71d0503b078581ecc2fd92e607fd2cc6caf8b32c1e66bf61563f1d3ebea3a89 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-green-final-lifecycle-01/wp02-26.trx | 07e6fc82a4b102a110c773d8ef0ea9ff95b497394bc2159a558d47363151c7c5 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-cleanup-reentry-01/exit-code.txt | f1b2f662800122bed0ff255693df89c4487fbdcf453d3524a42d4ec20c3d9c04 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-cleanup-reentry-01/source-inputs.json | 80f1211d58b13f20a18b509b9e85e99d0bb50e1c5d3dd01ab941ee72528a5e26 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-cleanup-reentry-01/wp02-26.trx | ed711b999cf68829549de1d20279baf339dd9a2ecf379f4e9c0c3ed9e1830230 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-completion-01/exit-code.txt | f1b2f662800122bed0ff255693df89c4487fbdcf453d3524a42d4ec20c3d9c04 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-completion-01/source-inputs.json | 63e39301d5a8771bae0f420dbc8e17ec2cc2e032ab7d384cab1838c98ad3b2c1 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-completion-01/wp02-26.trx | bed88a19d5d21ed526d80eb3489759f339b1ffb40e1d620077e15a69cd748c45 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-numeric-budget-01/exit-code.txt | f1b2f662800122bed0ff255693df89c4487fbdcf453d3524a42d4ec20c3d9c04 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-numeric-budget-01/source-inputs.json | f754711170daf5f3a2d22c59091d23f0a3acaba95554fb626bc223a248767500 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-numeric-budget-01/wp02-26.trx | 2880b8e8d933ee92e753b5d2a04584cdc442f92d50a39c959cdfbe13a7b67ebb |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-observer-reentry-01/exit-code.txt | f1b2f662800122bed0ff255693df89c4487fbdcf453d3524a42d4ec20c3d9c04 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-observer-reentry-01/source-inputs.json | bf8d8ac399837bbd7b68f9419598af265775b478ebe4537e6c1dda425f5be495 |
| docs/development/evidence/WP02-26/20261015T001945Z/implementation/unit-red-observer-reentry-01/wp02-26.trx | 54d0ef30220afc70f0ccf000ebea9211e27821c0761659af1e91fc465425d28a |
| docs/development/evidence/WP02-26/20261015T001945Z/inputs/original-PLT-OBS-01.md | 20c9a128b3f2bab0621459cc6c6494b16fb21cd8340792738ba89e90efa3bc99 |
| docs/development/evidence/WP02-26/20261015T001945Z/inputs/original-PLT-TEST-01.md | d8b4ef19a1b3a0a528ddd7831f5159295d75b7eecadfa787e36ad028d362c5aa |
| docs/development/evidence/WP02-26/20261015T001945Z/inputs/WP02-26.md | ecc4fab40687bfe3dd918c35ef231faa6cb19ecca3f905dc446b75d19633f991 |
| docs/development/evidence/WP02-26/20261015T001945Z/pg-resource/ownership.json | 0c1549897fdd77800a98cfa77cdce9be5f3747d0359aef1c15c4fbef70ef4e13 |
| docs/development/evidence/WP02-26/20261015T001945Z/pg-resource/provision-history.json | 358a105f05bd318b8cd2ff4b3cb7f774e73011ce08688dc03eddbefe2c63afa3 |
| docs/development/evidence/WP02-26/20261015T001945Z/reviews/spec.md | b68bb2d75bc3d866cc515e4379d6065c33bc86d55d79c2a89b5a4c44b1f6c059 |
| docs/WP02-26-SLO-TEST-EVIDENCE-V1.md | 744afb62ad61e7d969f4cd0d2e2136f713efdc50ddc2dc0e66b3213f8e202930 |
| src/CP6.Platform.Contracts/CP6.Platform.Contracts.csproj | 3c71927da98b44f52abb45e29f1da302b7bbfcab28077c365ad09bdfac8dd55c |
| src/CP6.Platform.Contracts/SloQualificationV1/ExactNumber.cs | e3127e5bd47588c6d11ccd5692494f775642372e0558302192c943a7d576a424 |
| src/CP6.Platform.Contracts/SloQualificationV1/SloQualifier.cs | 27dd5e8bc6c08056648c4514d2575aeda1a953af5be839d09cbd96135b26c1be |
| src/CP6.Platform.Testing/CP6.Platform.Testing.csproj | dfc8d21583474e7ebed6d2d7c83e8b5010fde2663a24f385ace9b73e90c46044 |
| src/CP6.Platform.Testing/Cp6HttpFaultScript.cs | a9ea7ec30ca6c680d2e926e12e40a120e937b39c4ce8ee10c6976895cb1e983a |
| src/CP6.Platform.Testing/EvidenceV1/DeterministicFaultCase.cs | 2564301c8120583ab9301a5a6a8adafa56cbda1ec0e82f8676e0a8ac9efa909c |
| src/CP6.Platform.Testing/EvidenceV1/EvidenceCase.cs | a295f500a06d3dad3a052f4186dda25101081f08f9ab2cb938e4be99c472356c |
| src/CP6.Platform.Testing/EvidenceV1/ExactValue.cs | 54589df207dbbffaa7c5329de664443414554117eed54c6d62ee198d0e83a95a |
| tests/CP6.Platform.WP0226.PostgreSqlTests/audit_workflows.py | aba0b87dec6fe5b84055b4be0dae79e153722c826a6b08e4891bdc5c7d1c8b9d |
| tests/CP6.Platform.WP0226.PostgreSqlTests/collect_resource_evidence.py | 818595dc16c5225fd81d8583660f965b8bfdad58b89340d542c80a086cb879f3 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/CP6.Platform.WP0226.PostgreSqlTests.csproj | 264c4e743a710c00be3f611f52f3413ca609bd0c0d76c7112faf717a2e49510e |
| tests/CP6.Platform.WP0226.PostgreSqlTests/Directory.Packages.props | 2fb217b26add88c81381e3079b956d6445b82a0a18cf404a44c9777f52ee0085 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/map_acceptance.py | 5213cce41de27c9ba989657d94aa8a948ecc8512cb3e2625a9f53e63ea97eb5d |
| tests/CP6.Platform.WP0226.PostgreSqlTests/NativeEvidenceTests.cs | 92147bbc0d6766c0598c07cb1f5e00c64e4f87817c3a6eddd0a581dbaac05519 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/pg_resource.py | 2673eb2b06349f6a1eb2c0f4533013a55ddc6877a041bbb2b5492cc7cf6d6668 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/PgFixture.cs | fafe0029d80dfa3ff5b9a600e5f6c32a26523bf3c69d1cd7ef7c7e1d5464013c |
| tests/CP6.Platform.WP0226.PostgreSqlTests/README.md | 0e30021972d31b5bdbdb67c3c6e8f0ee26223bf77df482eba6d7b0e293fd0b74 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/run_gate.py | 7aa3e5b0c77fdc1527c4ee558b1c64b28f2f71024d7fdb7f4233a61357ac469a |
| tests/CP6.Platform.WP0226.PostgreSqlTests/test_resource_guards.py | dea1096fe4dfbb04cd7e2e56a5cb9694f7ab63dcdb49434fefdfd2d6779b9a5b |
| tests/CP6.Platform.WP0226.PostgreSqlTests/verify_gates.py | b39d80518ecb6e2aca7abdd050e302358848de2e6661335a84fa0581ca674447 |
| tests/CP6.Platform.WP0226.Tests/CapabilityTests.cs | 6f63771fc7570f624f06df5648f4a474ca9d8d06d4ab0c785d7ec690d8025455 |
| tests/CP6.Platform.WP0226.Tests/CaptureTests.cs | dd7f61749f7836dd74c3ab3da09679684e4a83611a4c08b13990236026e6fdf0 |
| tests/CP6.Platform.WP0226.Tests/CP6.Platform.WP0226.Tests.csproj | b1e7ed02efe0e55f6caa6384daeb8994f27c963970da10368c75ef666aba674f |
| tests/CP6.Platform.WP0226.Tests/ExactTests.cs | e42acd924c2c91d9f128332b6abc691ad08fc3f3631413a9000a01370e6964bd |
| tests/CP6.Platform.WP0226.Tests/FaultTests.cs | ba5752e6bed14bb028bc39c0d13ffb1f40e53f70bec3e6a3bbc22f75e7c89aab |
| tests/CP6.Platform.WP0226.Tests/QualificationTests.cs | ff4fc056f9a49030c7a18919101b7a987f56e9560798fee81560289669563bbe |

## Scoped follow-up: evidence mapper correction

Verdict remains PASS. This follow-up reviewed only the map_acceptance.py and verify_gates.py working diffs plus the regenerated matrix and mapper execution evidence. No .NET/library change, full review restart, test execution or PostgreSQL action was performed by this reviewer.

The original mapper's duplicate evidence keyword caused an actual TypeError; matrix-red-01/failure.json preserves exit 1 and identifies its original source hash. The correction keeps every original catalog field, including evidence, and adds scoped execution links under local_evidence. Existing matrix-green-01/execution.json records exit 0 for the corrected mapper; its output reports 56 cases and 26 scoped local portions.

Independent read-only checks of acceptance-final.json confirmed 56 unique original IDs, zero changes to any original catalog field, all 56 original_business_acceptance values still NOT_RUN, 26 LOCAL_PASS portions, and no non-Passed local proof entry. Both referenced unit/PG TRX byte hashes match the matrix links. This mapping does not elevate any original business acceptance.

The gate verifier now excludes auxiliary Python evidence writers/auditors from the native test assembly's relevant-input comparison. PgFixture.cs:29,38 invokes only pg_resource.py, which remains checked; all library source, test C#/project/config, resolved dependency and local assembly identities remain checked. No source snapshots were rewritten by this correction. The relevance decision is valid for these reviewed inputs and does not exempt a future Python dependency actually used by tests. Separate mapper execution evidence identifies the changed mapper bytes; native PostgreSQL tests do not need rerunning solely for this evidence-writer correction.

Follow-up reviewed working/evidence hashes (supersede earlier hashes only for the two changed scripts):

| Path | SHA-256 |
|---|---|
| tests/CP6.Platform.WP0226.PostgreSqlTests/map_acceptance.py | d234cd915114c79bfdbd36722e767004cafbb9db00a53261d56f1d39a438efc7 |
| tests/CP6.Platform.WP0226.PostgreSqlTests/verify_gates.py | 7a2f8c468a3995aef98d433168ee7f8b42f2a62c11f9dff17ff595ede78fa5c5 |
| docs/development/evidence/WP02-26/20261015T001945Z/inputs/catalog.json | 3dab4365b3c4691a0d859fa232c4907d26488de5ba92ad2a5736397fbb8cdc2c |
| docs/development/evidence/WP02-26/20261015T001945Z/acceptance-final.json | d4496c59034090d7ac9c8a549dcf435b971399bd7144196ca530731a3680cb02 |
| docs/development/evidence/WP02-26/20261015T001945Z/matrix-red-01/failure.json | c359781ecd7ac736ed2c1cf696822900d14e2b3c513ac6880bdc78777df6e213 |
| docs/development/evidence/WP02-26/20261015T001945Z/matrix-green-01/execution.json | 7768d5128fd1df1f7f2a0729f882741f6c019b56758525def5113a033f6062ca |
| docs/development/evidence/WP02-26/20261015T001945Z/matrix-green-01/output.log | 518a14ccc96b0c26cde67f234f91621a3769dfab1feb01b62a1f3a8ca698aaa0 |
| docs/development/evidence/WP02-26/20261015T001945Z/final-01/unit/wp02-26-unit.trx | f2867b9ffd92c30fef4741b56bcf1e60b51feec112b7fb1786011888826c24b8 |
| docs/development/evidence/WP02-26/20261015T001945Z/final-01/pg/wp02-26-pg.trx | 06531f888807e5f7f23e4cdba57872879472a55f2accb8d269adc71e7fea2ae3 |
