# WP02-27 task inspector

This local evidence helper is not a package publisher or shared ReleaseTool change.
Build using the repository SDK, then invoke its DLL through the same dotnet host.
Set `DOTNET_ROOT` and prepend that SDK installation to `PATH` for subprocesses.

```
dotnet build eng/wp02-27/Inspector/Inspector.csproj -c Release
dotnet eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll formal REPO INVENTORY EXPECTED_INVENTORY_SHA256 OUTPUT DOTNET RELEASE_TOOL_DLL UTC
dotnet eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll local REPO PACKAGE_DIR SOURCE_SHA VERSION OUTPUT
dotnet eng/wp02-27/Inspector/bin/Release/net8.0/Inspector.dll fixtures REPO OUTPUT UTC [ORIGINAL_NUPKG]
```

Use an absolute, new output directory for each invocation. UTC must have the exact
form `2026-10-13T12:00:00.000Z`. No existing evidence file is overwritten.
`formal` requires an independently supplied expected SHA-256 of the captured
`inputs.json`; it checks original package, publication and current policy hashes
against that inventory before constructing the approved matrix. All seven actual
archives go through the unchanged CLI in Current mode. Each CLI call preserves
arguments, evaluation/actual times, exit, stdout, stderr and checked output identities.
Any individual failed verification makes overall trust Unknown; the remaining
packages are still attempted. Exit 0 means all seven currently verified, exit 3
means the core returned Unknown current trust, exit 2 means validation/helper failure.
The matrix, approval provenance, lock and state are saved. Failures after matrix
construction instead retain a bounded diagnostic if no lock can be issued.

`local` expects exactly seven nupkgs from one explicit source/version. It compares
the two new Release schemas against actual source bytes and inspects PE metadata
directly from archive DLL bytes: S01-09 APIs in **Messaging**, context contracts in
Contracts, and the context executor in Abstractions. No DLL/source fallback is used.
This proves public type/method-name presence, not API behavior or full signature
qualification. Native profile schema packaging remains an explicit gap; the new
Release schemas are never substituted for INQ/ERP native schemas. Compatibility
rows stay empty/Unknown and adoption stays Unknown; task-local publication and
formal trust stay absent.

`fixtures` creates Current/Historical/Revoked/UnknownSigner policies using copied
public original DER and one ephemeral in-memory synthetic second Current key.
Only public DER is written; the key is disposed without export. All fixtures are
parsed through the existing policy validator. If an original signed nupkg is
supplied, separate timestamp-missing and malformed-timestamp copies are generated
by editing unsigned CMS attributes. Original bytes, keys, stores and trust policy
are never changed. These are test fixtures, never new trust roots or signed releases.
