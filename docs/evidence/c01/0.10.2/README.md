# C01 formal 0.10.2 publication evidence

The seven packages were built once and published through the existing protected
[formal workflow run 34417259187](https://github.com/GTX537/CP6.Platform/actions/runs/34417259187),
attempt 1, from `main@fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`.
Owner `GTX537` approved `p10-formal-release`. Both `sign-publish` and
`verify-linux` completed successfully. The workflow blob is
`8683248e8347b1f42ae65cc32cd40a34aa30b871` at
`.github/workflows/p10-formal-packages.yml`.

The source passed all five required jobs in [PR run 34415260032](https://github.com/GTX537/CP6.Platform/actions/runs/34415260032)
and [exact-main run 34416274332](https://github.com/GTX537/CP6.Platform/actions/runs/34416274332).
Its merge tree equals the reviewed [PR #55](https://github.com/GTX537/CP6.Platform/pull/55)
head `0660001cb1357a5871d5f690e6a709163a66ccac`. A fresh merged-source
registration smoke passed 54/54, without failures or skips.

## Original immutable artifacts

Both artifacts belong to the publication run above and expire at
`2026-12-08T23:30:49Z`. Downloaded archive SHA-256 was checked before opening
the ZIP or extracting any entry. The Windows archive contains exactly 15
bounded files; the final archive contains exactly the five records below.

| Artifact | ID | Archive bytes | SHA-256 |
| --- | --- | --- | --- |
| `p10-s04-windows-readback-fbcd21528078a04e5b53c42c5fdfebe6ffa9655f-1` | `10130186392` | 391209 | `2d479f4008c3794b601998b67908f77022c21a33c81053965fd3c632fe4972ff` |
| `p10-s04-final-publication-fbcd21528078a04e5b53c42c5fdfebe6ffa9655f-1` | `10130203986` | 6172 | `3a1c3b5df3403d142e9a5b9f8477704e4cbd229a896cb619d8f7e9ba3026c854` |

The five JSON files here are unchanged copies from the final archive. No
timestamp, source identity, package hash or toolchain field was rewritten.

| Record | Bytes | SHA-256 |
| --- | --- | --- |
| [Publication](formal-package-publication.v1.json) | 7550 | `17fd39f5724ba118771eb95aeb18d78045ea2e576431957ab7c24b8d13719181` |
| [Build invocation](build-invocation-provenance.v1.json) | 4132 | `4452b421b21dd72e09cf961dca4e07faed4efc7fca25a9c197d9def7d84c8bc3` |
| [Feed read-back](formal-package-readback.v1.json) | 6843 | `f029d7e00ea0a3404e52e80615ad93f1b38065253364c140601b5ce58fc952c9` |
| [Windows verification](formal-package-verification.windows.v1.json) | 5507 | `3117f1c5939e7792de5689b11de0f21d7e75593c72820e8e57dff14a77188488` |
| [Linux verification](formal-package-verification.linux.v1.json) | 5038 | `943b7d7836a770d10ee6d40e82bfb8e0890b5235f3918ab0fbaeb4b73763adfa` |

## Published package subjects

Every row is version `0.10.2`, source
`fbcd21528078a04e5b53c42c5fdfebe6ffa9655f`, in the authoritative
[GitHub Packages feed](https://nuget.pkg.github.com/GTX537/index.json).
The author-signed and published/read-back hashes are equal. The actual
downloaded package files were checked against these hashes and accepted by
the signed seven-package verifier.

| Package | Published bytes | Author-signed and published SHA-256 |
| --- | --- | --- |
| `CP6.Platform.Abstractions` | 18925 | `e838ba9c9c6e434f8c8a2bf41e9da7d931a962400435aabb2189f40628c17103` |
| `CP6.Platform.AspNetCore` | 62801 | `5dc9cee4335e69f463361afc89d36587650b896059472570753ef485787fec73` |
| `CP6.Platform.Contracts` | 41910 | `84b62cad23d0ce09a4af545ac5ae73cd625fce33eb77d022740350dac0364570` |
| `CP6.Platform.Deployment` | 75087 | `231a7cb3e18fd8ba9a7d60bbe01195ca7676c33b55b542a294c4a76c0fff066b` |
| `CP6.Platform.EntityFramework` | 54883 | `cc780ce1b454c62f96caa990eb2e0462563b1917e8c505b11100a4555d5b4544` |
| `CP6.Platform.Messaging` | 55133 | `878994735a171194b8944c58fa919508380fa7d0707aa09176427295d0aab235` |
| `CP6.Platform.Release` | 116423 | `0b8fd3b332c837e6f1c0cbe9c312e87a671c4f119d2128768f7284d4fa26a8f1` |

## Trust and validation

The unchanged public trust policy SHA-256 is
`da359e3a8e9be2220541c53613d2da277cb2bb9a22a8770df30c808a033b953f`.
The signer certificate fingerprint is
`1debfb8ff286ea51192b7f259d1ac823c105c4188eac40148598d37f0e20ff0d`.
Trust remains `PinnedSelfSigned`, `internallyTrusted=true` and
`publicCaTrusted=false`. All packages require RFC3161 timestamps with policy
OID `2.16.840.1.114412.7.1`. The publication record reports producer SDK
`8.0.425`, NuGet `6.11.2.1` and runner image `windows-2025`.

From the repository root, build the Release tool using the supported .NET 8
selection, then validate the unchanged publication and public trust:

```powershell
dotnet build tools/CP6.Platform.ReleaseTool/CP6.Platform.ReleaseTool.csproj --configuration Release
$evaluationUtc = [DateTimeOffset]::UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", [Globalization.CultureInfo]::InvariantCulture)
dotnet tools/CP6.Platform.ReleaseTool/bin/Release/net8.0/CP6.Platform.ReleaseTool.dll validate-formal-publication docs/evidence/c01/0.10.2/formal-package-publication.v1.json eng/p10/trust/p10-formal-nuget-trust-store.v1.json eng/p10/trust/certificates $evaluationUtc
```

The `0.10.2` version slots have been consumed. Do not rebuild, republish or
overwrite these packages. Historical `0.10.0`, `0.10.1` and P09 evidence retain
their original identities. CRM must still restore the exact new feed bytes
and validate this evidence using the new Release package and its packaged
Schema, followed by real issuer-to-consumer acceptance. This publication does
not close C01 or authorize a deployment.
