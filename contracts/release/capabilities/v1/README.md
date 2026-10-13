# WP02-27 capability and compatibility v1

This is a separately versioned, local Release contract. It does not alter historical
formal-publication v1, its version allowlist, public media types, trust roots, or P10
publication authority. The two JSON schemas are packed under this directory in
`CP6.Platform.Release`.

## Catalog and scope

The catalog identifiers below belong to this new matrix, not to older formal
publication records. Both capability versions are `1.0.0`.

| Capability | Exact profiles | Scope and source |
| --- | --- | --- |
| `S01-09` | `crm-inq-r3-v15`, `erp-credit-current-v1`, `erp-credit-frozen-r8` | Native contract routing and original JSON byte capture; source delivered through S01-09, integrated before Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`. The routing catalog uses `CP6-S01-CONTRACT-INPUTS/1.0.0`. |
| `ctx-binding-v1` | `ctx-v1` | Context binding for `Background`, `Http`, `Message`; `ctx-v1` is the wire profile, distinct from this catalog's capability version. Integrated at Platform `9259a45d39bae7e98ca804a74778a588c1ad4619`. |

These scopes do not establish a complete business command validator, permission to
write, a shared transaction/fence, production host registration, or consumer adoption.
Formal `0.10.2` was published from
`fbcd21528078a04e5b53c42c5fdfebe6ffa9655f` and does not include either later capability.
All supported historical formal versions therefore require empty capability and
compatibility lists here. A future formal version requires a separately reviewed
catalog/publication change; a source commit or local pack cannot authorize one.

## Caller responsibilities and verification boundary

`ValidateShape` accepts only already-canonical deterministic JSON and closed fields.
It returns an owned copy of the same bytes. JSON Schema enforces structure; the API
also checks ordinal sets, exact cross-field identity equality, source/packaged schema
equality, evidence membership, duplicate/conflicting compatibility decisions and
the lane-specific feed identity. Schema validation alone does not prove those
relations, archive contents, cryptographic signatures, current trust or adoption.

`Evaluate` requires `Cp6CapabilityApproval` from trusted caller configuration.
Pin `ManifestSha256` and `SourceGitSha` after independently reviewing the source,
package set and measured compatibility evidence. Never build this approval by
copying hashes from the untrusted input being checked. Approval of a matrix asserts
that its exact compatibility rows were measured; evidence file presence or a
syntactically valid SDK/version string is not measurement. A request must match
every byte of an approved canonical row, including producer, consumer/source,
route, profile, SDK/runtime/OS/architecture, schemas, semantic validator, host,
configuration, message versions, PostgreSQL driver/migration and evidence identities.
Every other combination is `Unknown`; no major-version compatibility is inferred.
`Blocked` and `Unknown` rows require a reason. No result enables a consumer.

The caller supplies exactly seven actual archive byte sequences keyed by package
ID, plus the exact content-addressed evidence byte set. Evaluation snapshots them,
checks SHA-256 and nuspec ID/version/repository/full lowercase source SHA, rejects
duplicate ZIP entries, and checks the actual schema entry bytes for every row.
The matrix binds each row's producer to the enclosing immutable package set; rows
cannot substitute their own package version/feed/source. Source schema bytes must
be part of the supplied evidence and identical to the packaged schema bytes.

Task-local versions have the exact grammar
`0.10.3-wp02.27.gFULL_LOWERCASE_SOURCE_SHA.SEQUENCE`, with a positive sequence and
the same full source SHA as the matrix/nuspec. Allocate a new sequence for a different build; the approved matrix hash rejects changes under
an existing accepted identity. Their feed is the logical namespace
`task-local:wp02-27#PACKAGE_ID/VERSION`, never a public feed. They always return
`NOT_PUBLISHED` and `NOT_FORMALLY_TRUSTED`; no callback can promote them.

For historical formal identity evaluation, provide original publication bytes and
the current pinned policy plus its independently approved SHA-256. The existing
publication's SHA-256 must also appear in the independently approved matrix's
`evidenceSha256` set, with those exact original bytes supplied in the evidence map.
Do not approve a replacement publication by deriving new evidence from the input
being evaluated. Changes to workflow/run metadata or creation time require a new
independent approval even when package signatures, source and versions are unchanged.
The existing
immutable formal validator is composed in Current mode. Policy mismatch, historical
or revoked signers, unknown roots, expired signer validity, future publication,
or changed publication/package/source/feed identity fail before the crypto boundary.
`ICp6CapabilitySignatureVerifier` is an explicitly trusted **host-owned** integration
boundary. It must execute the existing ReleaseTool formal package verification
against the exact supplied original publication, seven archives, policy and
evaluation time; it must not deserialize JSON success fields as signature proof.
No production adapter is supplied by this contract. Without such a verifier,
formal shape/byte validation leaves `TrustCurrentlyAccepted=Unknown`. Unit-test
stubs prove boundary plumbing only, never a real signature verification.

## Status and consumer lock

`SourceFrozen`, `ContractShapeValid`, `ArtifactBytesVerified`, and
`TrustCurrentlyAccepted` are separate. `SourceFrozen` means the source identity
matches independently approved configuration, not a new Git source audit.
`ArtifactBytesVerified` means archive identity and referenced schema bytes match,
not that every API or behavior has been qualified. `ConsumerQualified`,
`HostRegistered`, `ConfigEnabled`, `RuntimeObserved`, `BusinessApplied`, and
`IndependentlyAccepted` remain `Unknown`; this API cannot grant those stages.

The deterministic consumer lock binds exact matrix bytes, requested-row bytes,
source/repository, package/feed/hash identities, evidence hashes, and (for formal
evaluation) publication/policy hashes. Its returned bytes are defensively copied.
It contains no signature or current-trust grant and is not a permission token.
Re-evaluate current trust at use time; do not reuse a lock as proof that a signer
remains current. Trust evaluation time is passed directly to the host boundary,
not manufactured as a publication/run identity or encoded as permanent acceptance.

The synthetic tests under `CapabilitiesV1` establish local contract behavior only.
Actual signed seven-package verification and local task-specific pack evidence are
separate delivery evidence. Main/CRM adoption, WP02-02, business acceptance,
database migration, host configuration and publication remain outside this API.
