# WP02-02 wave8 implementation verification

Status: **DIAGNOSTIC_ONLY; mandatory Release gate BLOCKED**. No publication, signing, Actions dispatch, deployment, or online Current validation was performed by this work.

## Boundaries

Readers register exactly 0.10.0, 0.10.1, 0.10.2, and 0.10.3. Historical versions retain empty formal capabilities. Formal 0.10.3 permits only ctx-binding-v1 / 1.0.0 / ctx-v1 / Background,Http,Message. TaskLocal remains untrusted and untested consumer combinations remain Unknown. No S01-09, Transport, OBS, SLO, runtime, or Main business acceptance is granted.

Formal pack/sign/publish entry parameters permit only 0.10.3. Test-P10FormalPackageSet and New-P10FormalPublicationRecord are reader/recovery entry points and permit all four registered versions, preserving historical publication-record recovery. Workflow and script changes are limited to version registration. Recovery workflow and trust assets were not edited.

JSON Schema enforces the registered catalog, package version/feed grammar and exact formal package/feed pairing. Runtime validators additionally enforce cross-field source/hash equality, distinct package hashes, actual archive bytes and independent evidence/current policy. Schema validation alone never establishes trust.

## Commands and evidence

All execution used Windows and SDK 8.0.424. PATH was prefixed with the SDK directory so child dotnet invocations use the same SDK. No Linux run is claimed.

The original command was `dotnet test tests/CP6.Platform.ReleaseTests/CP6.Platform.ReleaseTests.csproj --configuration Release --filter <targeted-filter> --logger trx;LogFileName=red.trx --results-directory <this-directory>` without a warning override. Restore failed before tests due to existing ReleaseTool System.Security.Cryptography.Pkcs 8.0.1 NU1902 and NU1903 promoted to errors; see `red.log`. No dependency or audit policy was changed.

Subsequent test commands added `-m:1 -p:UseSharedCompilation=false -p:WarningsNotAsErrors=NU1902%3BNU1903`. They preserve the vulnerability warnings and are diagnostic only, not mandatory gate passes. The earlier diagnostic attempt used shared compilation and was interrupted at the parent process after slow compiler progress; its already-started test child completed and retained `red-tests.log` and `red-tests.trx`. The explicit local compiler run is `red-local-compiler.log` / `.trx`.

RED targeted filter:

```text
FullyQualifiedName~P10FormalRegistrationTests&FullyQualifiedName!~Actual_task_local|FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~ReleaseSchemaAssetTests|FullyQualifiedName~Formal_workflow_is_manual|FullyQualifiedName~Formal_shape_and_success_claims
```

RED observed missing 0.10.3 support in API/schema/version registration before production changes. Both RED logs contain 90 tests: 56 passed and 34 failed. Some failures were an isolated test harness schema-registry conflict, subsequently fixed with per-evaluation registries. `red-summary.json` separates this from actual registration RED; the full failing count is not claimed as missing production behavior.

Targeted diagnostic filter:

```text
FullyQualifiedName~P10FormalRegistrationTests&FullyQualifiedName!~Actual_task_local|FullyQualifiedName~FormalPackagePublicationTests|FullyQualifiedName~ReleaseSchemaAssetTests|FullyQualifiedName~P10FormalWorkflowContractTests|FullyQualifiedName~CapabilitiesV1
```

Result: 171/171 passed, zero skipped, in `targeted-diagnostic.log` / `.trx`. Four added semantic/current-boundary regression cases and actual packaged-schema validation are covered by the subsequent safe suite rather than retroactively attributed to this run.

Safe Release diagnostic command uses the same project/configuration and diagnostic flags, plus `CP6_FORMAL_REGISTRATION_PACKAGE` set to the actual package path in `tasklocal-pack.json`, and filter:

```text
FullyQualifiedName!~Formal_verifier_accepts_one_real_RFC3161_timestamp_and_rejects_identity_and_integrity_mutations&FullyQualifiedName!~Test_package_set_has_exact_seven_ids_one_version_one_source_and_test_only_trust&FullyQualifiedName!~Injected_signing_failure_removes_private_material
```

The three named signing tests are NOT_RUN because signing is outside this task's authorization. They are exclusions, not successful or skipped tests. Safe-suite result: **353 passed, 2 failed, 0 skipped, 355 executed**, in `release-safe-diagnostic.log` / `.trx` and `safe-suite-summary.json`. The actual TaskLocal packed-schema test passed. Both failures are in P10PackageTests.cs, outside the assigned edit window: the former unknown-version case still rejects 0.10.3, and the package asset allowlist omits the existing capabilities/v1 assets. These failures remain visible; the suite is not green.

## Actual unsigned TaskLocal package

`eng/wp02-02/formal-registration/implementation/pack_tasklocal.py` creates an unsigned unique prerelease only. Reproduction selects SDK through CP6_DOTNET or PATH. Exact executed pack command, SDK, dependency libraries, nuspec, package identity and every original source input hash are in `tasklocal-pack.json`; log is `tasklocal-pack.log`. `pack-source.diff` records the dirty production source/schema delta. The baseline Git SHA alone does not identify these working-tree bytes.

- Version: `0.10.3-wp02.27.g66480fe24d51c706708be971e7e9d75a684a3bc4.1792043257802062`
- Package SHA-256: `49ba29abe9eb050e54896229cec539144b188d4392d442fe34c6d30c3e97c24f`
- Base Git SHA: `66480fe24d51c706708be971e7e9d75a684a3bc4`; dirty candidate, not published source.

The package is retained under ignored artifacts and is not committed or uploaded. Actual archive tests compare raw source and packaged schema bytes and evaluate positive and negative documents. Synthetic certificate/compatibility fixtures do not prove production cryptography or consumer adoption.

Six modified PowerShell scripts passed parser-only validation (`powershell-parse.json`); no formal script was invoked. `git diff --check` passed. `validation-inputs.json` records final implementation/test/contract/dependency input hashes.

## Review correction

The initial implementation treated New-P10FormalPublicationRecord as a 0.10.3-only writer. Task-level review clarified the specific approved proposal classifies it as a historical reader/recovery entry; it was corrected to `[0123]`. `record-reader-regression.log` / `.trx` covers only the six script parameter contracts after that correction: 6/6 passed, zero skipped (DIAGNOSTIC_ONLY). The complete safe-suite result above predates this correction and is not relabelled as a rerun. Production library/schema/package inputs did not change, so actual pack and unrelated test evidence remain applicable.
