# Native WP02-26 fixture

This is a synthetic fixture Owner using installed PostgreSQL 18 on loopback 55429.
It does not connect to application databases, use EF models, or prove Main/CRM adoption.
No commands delete a cluster, reset schemas, truncate receipts or terminate unknown sessions.

Use `pg_resource.py create` once for a fresh full owner token and contained
`D:/CP6-local-test/WP02-26/<owner32>/` directory. The helper checks the reserved port
and creates an ACL-restricted private directory before generating fresh SCRAM passwords.
It prints only the resulting private settings path. Do not publish that file's contents.
Setup/admin and DML runtime roles are distinct. No role membership, CREATE/TRUNCATE,
superuser, role/database creation, inheritance or bypass-RLS permission is given to runtime.
The immutable resource guard is readable by runtime but writable only by setup.

Every later test connection verifies marker, system identifier, actual data directory,
database OID/name/owner, endpoint and session_user. Resource actions additionally check
the entire active client-session inventory. Normal verify/stop requires zero other sessions;
controlled crash allows only explicitly named PIDs whose database, role and application
name match this fixture's crash operation. The stopped configuration is hash-sealed:
changing it cannot silently start a different endpoint or preload code. Startup checks
the sealed configuration, system identifier, absent PID file and free port before launch.

```powershell
$python = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe'
$helper = 'tests/CP6.Platform.WP0226.PostgreSqlTests/pg_resource.py'
& $python $helper create
# Set this only to the newly returned private settings path, never a different task.
$env:CP6_WP0226_SETTINGS = '<newly returned path>'
& $python $helper verify $env:CP6_WP0226_SETTINGS
& $python tests/CP6.Platform.WP0226.PostgreSqlTests/run_gate.py pg --label '<new-run>'
& $python $helper stop $env:CP6_WP0226_SETTINGS
```

The six tests check actual lock contention/idempotent duplicate keys, exact stored numeric
text and bytea, native RepeatableRead serialization failure, technical success followed by
actual rollback, injected application ACK suppression followed by original receipt readback,
runtime permission rejection, and controlled crash/restart. Exact values are passed as
text parameters cast to NUMERIC and read as text, preserving precision beyond decimal/double.
Credit fixture uses NUMERIC(38,8); inventory fixture uses unbounded NUMERIC, without inventing
a total-digit limit for the accepted inventory contract. Original wire bytes remain separate.

Crash uses `pg_ctl -m immediate` on the verified own cluster. PostgreSQL documents that this
requires [crash recovery on restart](https://www.postgresql.org/docs/18/app-pg-ctl.html).
The SQLSTATE 40001 test uses real overlapping RepeatableRead transactions, following the
[native isolation semantics](https://www.postgresql.org/docs/18/transaction-iso.html).
An application TimeoutException deliberately thrown after a successful COMMIT is labeled
injected ACK suppression, never a native PostgreSQL error or an actually ambiguous wire COMMIT.

The fixture retains every case's rows with fresh case IDs; it does not clean old rows to make
tests pass. Every failure stays in its original run directory. Normal shutdown uses smart-stop
after proving zero unexpected sessions, and preserves data plus private server logs.

Partial provision is fail-closed. `recover-initialized <stage-...-Allocated.json>` is limited
to a recorded initialization timeout, same owned marker, stopped PG18 control file, and
SCRAM configuration, without recreating the cluster. `recover-database <stage-...-Started.json>
--database-oid <previously observed OID>` requires the recorded system identity and an explicit
observed OID, zero sessions and no partially created fixture/guard schemas. These paths do
not accept missing OID as runtime identity. Other partial states require inspection; never
delete or overwrite them. Private logs can contain provider details; only reviewed, safe
identity/failure excerpts are copied into repository evidence.
