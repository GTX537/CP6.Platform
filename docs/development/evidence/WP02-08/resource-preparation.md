# WP02-08 isolated resource preparation

The task-private PostgreSQL resource was created on 2026-10-14 under a fresh owner directory. It is intentionally left running for the parent implementation agent. This report records resource preparation only; no product test, original business acceptance, Main/CRM adoption, actual broker delivery, release or deployment is passed by it.

- Endpoint: `127.0.0.1:55428`.
- Owner: `dcdfb04dda48edc0d52d638b5e7bd2f0`.
- Private settings path: `D:/CP6-local-test/WP02-08/dcdfb04dda48edc0d52d638b5e7bd2f0/settings.json`. Only the path is disclosed; private settings, passwords, passfile and connection strings remain outside the repository.
- Runtime uses its own fresh SCRAM credential and has no superuser, database/role creation, inheritance, RLS bypass, database schema creation or fixture schema creation privileges. Administrative setup and runtime identities are separate.
- Guard schema: `wp0208_guard`; fixture schema: `fixture`. Runtime has guard SELECT and fixture DML/default sequence permissions only.
- Actual server system identifier, database OID/Owner, principal and marker are recorded in [resources/ownership.json](resources/ownership.json).

The new helper adapts the reviewed WP02-07 source, retaining hidden PowerShell `Start-Process` and uncaptured launcher file handles to avoid descendant pipe hangs. It does not inspect any previous cluster settings. It validates a fresh exact owner path, loopback port availability, private directory ACL before credentials, pre-start file marker, cluster system identifier, database OID/Owner, runtime identity and absence of other client sessions before handing over or stopping the cluster. Checks remain enabled with optimized Python. No process/session is terminated and no directory or database is deleted.

Actual verification:

1. Python AST parse passed, then `pg_resource.py create` completed successfully.
2. [Ten helper checks](resources/helper-verification.json) passed: live identity; wrong Owner, port, path, marker, system identifier and database OID rejection; refusal to read a foreign task settings path; occupied-port create rejection before mutation; teardown-preflight refusal with another live client. The owned three-second test session completed naturally; final verification found zero other clients.
3. MSBuild property/item evaluation with `C:/Users/tt/.dotnet/dotnet.exe` passed: inherited `net8.0`, `IsPackable=false`, `IsTestProject=true`, child Npgsql `8.0.8` and PostgreSQL provider `8.0.11` pins. No restore, compilation or test execution was performed in this preparation scope.
4. [Source inputs](source-inputs.json) record 36 admission-source entries against fixed Main `1ab2d031e3c501662ecb89a33839fac1cba55d33`, Platform `e5c63a28c94bae4cc66ec50fbe6079553c03d0d0` and Handoff `ad255c21810a101662fd3e380992a03fde71407e`. Raw working-copy hashes are distinct from fixed Git blob hashes. The original archived PLT-DEL source is local archive material rather than a tracked Main blob; its exact accepted raw SHA256 is verified and that distinction is explicit.
5. [Acceptance map](acceptance-map.json) copies every field of all 60 original `PLT-DEL-01` cases, including exact IDs, assertions, source pointers and source hashes. Original business status stays `NOT_RUN` for all 60. Library assertion/test mapping remains pending parent implementation.

Preserved preparation failures are in [resources/preparation-failures.json](resources/preparation-failures.json). The first source-hash attempt assumed the local original archive was a tracked Git blob and failed safely; provenance was corrected. The first helper-verification script resolved the repository root one directory too shallow and failed before reading settings or operating on resources; the parent index was corrected and the targeted check passed. Neither failure is represented as a product test result.

Reproduction scripts are [prepare-evidence.py](resources/prepare-evidence.py) and [verify-resource-helper.py](resources/verify-resource-helper.py), using `C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe -X utf8`. Source-map preparation is an initial generator: do not rerun it over later library assertion mapping. Helper verification requires an idle owned cluster; its deliberate busy-session check must not run concurrently with product tests.

The parent agent owns fixture/API/tests and eventual `pg_resource.py stop <private-settings-path>`. Smart stop revalidates identity and zero clients; logs/data remain available. No commits, push, Actions invocation/cancellation, workflow change, existing-database operation or production operation occurred in this preparation scope.
