# WP02-12 owned local qualification

All commands run from this task worktree with SDK8 and the cached dependencies/images.
Use a fresh 32-character lowercase hexadecimal owner. Never reuse the recorded delivered
owner for a new run, delete data, overwrite private settings, or target an existing service.
The private root and task ports are those assigned by wave7. No image pulls or Actions.

```powershell
$owner = [guid]::NewGuid().ToString('N')
$settings = "D:/CP6-local-test/WP02-12/$owner/settings.json"
$py = 'C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe'
& $py -X utf8 tests/wp02-12-runtime/pg_resource.py create $owner
& tests/wp02-12-runtime/Invoke-WP0212.ps1 -Mode ValidatePlan -Owner $owner -Settings $settings
& tests/wp02-12-runtime/Invoke-WP0212.ps1 -Mode Prepare -Owner $owner -Settings $settings
& tests/wp02-12-runtime/Invoke-WP0212.ps1 -Mode Broker -Owner $owner -Settings $settings
& C:/Users/tt/.dotnet/dotnet.exe restore tests/CP6.Platform.WP0212.Fixture/CP6.Platform.WP0212.Fixture.csproj --source C:/Users/tt/.nuget/packages -p:NuGetAudit=false
& C:/Users/tt/.dotnet/dotnet.exe build tests/CP6.Platform.WP0212.Fixture/CP6.Platform.WP0212.Fixture.csproj -c Release --no-restore
$env:CP6_WP0212_SETTINGS = $settings
& C:/Users/tt/.dotnet/dotnet.exe tests/CP6.Platform.WP0212.Fixture/bin/Release/net8.0/CP6.Platform.WP0212.Fixture.dll --initialize
& tests/wp02-12-runtime/Start-Fixture.ps1 -Owner $owner -Settings $settings
# Wait for /healthz to return the exact owner before starting dependent phases.
& tests/wp02-12-runtime/Invoke-WP0212.ps1 -Mode Dapr -Owner $owner -Settings $settings
& $py -X utf8 tests/wp02-12-runtime/verify_runtime.py --owner $owner --settings $settings
& $py -X utf8 tests/wp02-12-runtime/verify_quarantine_retries.py --owner $owner --settings $settings
```

Check each exit code; do not blindly execute subsequent commands after a failure.
The scripts preserve private command stdout/stderr and the native database; public
evidence must be explicitly exported with secret checks. `Initialize` creates the
fixture schema once; it does not reset an existing fixture. Setup is admin-only;
the app and data path use the restricted runtime identity.

Actual subscription metadata is checked against independent Kafka byte output.
The console observer is intentionally fixture-specific: one partition, bounded
record count, and reserved delimiters absent from the generated fixtures. It is
not a production consumer adapter. The Kafka admin is the fixture deployment
authority. Retention and cross-domain business idempotency are not under test.

Stop through the authenticated `/test/stop` endpoint using the task-private
control token after checking `/healthz` owner. Then run `Invoke-WP0212.ps1 -Mode Stop`
with the same owner/settings, and `pg_resource.py stop <settings>`. Native PG stop
requires exact cluster/database identity and zero unexpected sessions. Container
stop verifies full owner labels and image IDs. Never print tokens, copy private
configuration into Git, call compose down/prune, or remove retained data/logs.
