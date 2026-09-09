# C01 consumer contract and real issuer acceptance

Status: implementation detail for the accepted CP6 C01 design; independent specification review passed after four boundary clarifications on 2026-09-09. Preparation may run while the producer PR completes, but final acceptance must bind merged, verified producer and consumer commits. This is not package publication, full C01 completion, or deployment evidence.

## Inputs and observed baseline

- CP6 producer: PR #96 passed all seven PR checks and normally merged as `22453814f7d465abbf48ff6cb03e794765e490b1` on 2026-09-09. All five exact-main workflows passed, including Windows/Web, Android and real SQL, and a fresh merged-source identity smoke passed 116/116 without skips. Protected disclosure registration PR #97 and its exact-main checks passed. Final acceptance must bind this verified merged source or a separately verified successor.
- Platform main: `808a201f0cf6f877f8ca9c804e304585a29c446a`. Existing authentication tests pass 23/23, zero failures or skips, on .NET SDK 8.0.424.
- CRM main: `7651f4a1c65cae8604a8347ae6b872e6819ba800`. The actual API consumes `CP6.Platform.AspNetCore [0.8.0-alpha.2]`; the separate P10 consumer proof does not upgrade that API.
- The Platform bearer profile validates RS256, issuer, audience, lifetime and required claims, but does not constrain `typ`. CP6 user ID and access tokens share the CP6.Web audience. An actual issuer-to-consumer rejection test must establish this boundary before a fix is credited.
- The pinned JwtBearer 8.0.30 package uses IdentityModel Protocols 7.7.3. Its default manager returns current configuration after refresh failures and does not calculate a Cache-Control deadline. P03's delegation to the default manager is therefore not sufficient evidence for C01's bounded cache requirement. See the [pinned manager source](https://raw.githubusercontent.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/7.7.3/src/Microsoft.IdentityModel.Protocols/Configuration/ConfigurationManager.cs).

Actual local issuer diagnostics subsequently reproduced the gaps against the fixed published `0.8.0-alpha.2` assembly (`0.8.0-alpha.2+bfb0ebdc2e17f9a580156dbba6c0ce6cf6f3c672`, SHA-256 `97ef398fb509f49e006627f987a2bb3223a8eda217b23a74065cb1cc8eb1405e`). Actual password login, authorization-code/PKCE redemption by CRM, service issuance, both audience successes, mutual audience rejection and CRM service-token rejection passed. The four expected rejections returned HTTP 200: the actual issuer's ID Token, a signed unknown kid using trusted RSA material, and each audience with a trailing slash. The diagnostic reported 7 passed, 4 failed, 0 skipped; it is before-fix evidence, not final acceptance.

A separate actual CRM cache-only diagnostic used `AuthorizationUriAsync`, real issuer metadata/JWKS, controlled elapsed time and a transport outage after successful retrieval. It reported 6 passed, 2 failed, 0 skipped: the unmodified producer's 60-second `must-revalidate` boundary and subsequent retry-backoff request incorrectly retained stale trust. At 59 seconds the cache remained fresh without extra HTTP calls. A separately labeled permissive-header policy case passed at 899 seconds and rejected at 900 seconds. No token lifetime was involved in these cache observations. Both diagnostics used CP6 source `26be785313b5562a4cd9ca1aeb216c594cc8c70f` and CRM baseline above; final evidence must replace these preparatory identities.

The accepted CP6 design requires real CP6.Web/CP6.Services token validation, key rotation, Cache-Control freshness, controlled unknown-kid refresh, and a maximum trust age. Public sub/tenant semantics remain unchanged: a service subject is `service:<ClientId>` and must never be coerced into a user UUID.

## Verification boundary

`AddCp6JwtBearer` continues to be the Platform bearer entry point. It accepts only `typ=at+jwt`; identity tokens, missing types and unrelated types fail. Set `TryAllIssuerSigningKeys=false` and `IgnoreTrailingSlashWhenValidatingAudience=false` explicitly. Correctly signed negative fixtures must include an unknown kid using otherwise trusted RSA material, plus `CP6.Web/` and `CP6.Services/` audiences. Existing algorithm, required claim, issuer and clock-skew validation remain in force. Business authorization, permissions, revocation projections and service-specific scope checks remain downstream responsibilities.

Platform owns the bounded Discovery/JWKS manager used by its bearer registration. It implements `IConfigurationManager<OpenIdConnectConfiguration>` without a second IdentityModel last-known-good fallback that can outlive the manager's decision. Signature validation remains in IdentityModel. No application implements its own RSA signature algorithm.

Metadata must identify the configured issuer. The JWKS URL is fixed to the CP6 authority's `/.well-known/jwks.json`; requests cannot follow redirects or carry caller credentials. URI configuration rejects userinfo, query, fragment and unsupported schemes. HTTPS remains required by default; the existing explicit development metadata exception remains visible and tested.

Each response has a ten-second deadline covering headers and body and a 256 KiB body limit. JWKS contains 1–32 unique non-empty kids and usable RSA public keys of at least 2048 bits, with valid exponent and compatible `alg`/`use`. Private RSA material, invalid JSON, wrong metadata identity or unusable keys invalidate cached trust. No secret or response body enters public errors or evidence.

## Cache behavior

The shared consumer policy is explicit, with monotonic elapsed time for deadlines and an injectable TimeProvider for deterministic verification. A successful response never extends trust merely because a token was validated.

| Boundary | Value / behavior |
| --- | --- |
| JWKS freshness | Cache-Control max-age, capped at 300 seconds; absent max-age uses the existing 300-second fallback |
| Absolute maximum age | 900 seconds from retrieval, including response Age and elapsed time; repeated failures never extend it |
| Unknown-kid refresh | One refresh attempt per 60-second window, serialized with ordinary refresh |
| Network failure retry | At most one attempt per 30 seconds; cancellation by the caller propagates |
| Protocol/key failure | Clear prior trust; remain closed during retry backoff |
| HTTP directives | no-store retains no reusable keys; no-cache requires validation; must-revalidate forbids stale fallback after freshness expires |

For the actual CP6 issuer, `max-age=60, must-revalidate` means a refresh failure after 60 seconds rejects validation. A failed unknown-kid refresh while known keys are still fresh may continue validating a known key, but can never validate the unknown key. The 900-second cap applies as an outer bound where the response permits bounded network-outage fallback; it cannot override a stricter HTTP directive. This follows the chosen producer header and [RFC 9111's must-revalidate rule](https://www.rfc-editor.org/rfc/rfc9111.html#section-5.2.2.2).

The first request carrying an unknown kid may fail, as documented by P03. A subsequent request performs the controlled refresh; after new keys are obtained, later requests can validate them. Concurrent callers recheck state after entering the refresh lock. Warm-cache requests do not perform one HTTP fetch per token. Known metadata/cache failure exceptions must be mapped through OnAuthenticationFailed to an authentication failure rather than escape as HTTP 500; caller cancellation still propagates. Actual HTTP tests assert 401 and the existing generic Problem Details body for initial fetch failure, invalidated keys and expired trust. They must not accept an arbitrary non-success response as proof.

CRM's user OIDC client retains its nonce, UUID sub/sid, CP6.Web and CRM scope checks. Its response cache must honor the same producer directives; focused regressions precede any compatibility fix. Its browser paths must continue rejecting service tokens. No bearer token is accepted solely because another process accepted it.

## Real cross-repository acceptance

The runner uses actual merged CP6 issuer code, real isolated SQL data and actual password login followed by authorization-code/PKCE redemption for the user path. Service tokens come from the actual client_credentials endpoint. It obtains actual Discovery/JWKS responses over loopback HTTPS, using an existing trusted development certificate and temporary secrets kept outside Git. It never pre-mints a login session, replaces password authentication, or uses a fake metadata HTTP handler as the successful acceptance path.

The consumers are the fixed-version actual Platform package in a minimal HTTP resource host and the actual CRM OIDC client from a pinned CRM commit. A minimal host is allowed to expose the package's real authentication result; it must not reproduce its validation logic or overwrite TokenValidationParameters with a test validator. CRM must also restore its fixed package references from the actual feed for final evidence.

Fault injection may interrupt or corrupt the real metadata transport for negative cases. Valid success tokens must originate from the real issuer. Negative malformed token cases may alter fixture tokens; their results are identified as negative tests. Cache-age tests must prove the cache decision separately from token expiry, so an already-expired token cannot falsely satisfy a stale-cache assertion. Use CRM's AuthorizationUriAsync, which follows normal MetadataAsync(false), for cache-only observations. CheckReadyAsync always requests live metadata and ReconcileAsync also checks token lifetime; neither alone proves the normal cache's maximum age.

The unmodified producer's 60-second must-revalidate closure and the independent 900-second cap have separate evidence. For the cap, use a clearly labeled transport-header policy case that permits bounded outage fallback and demonstrate success immediately below 900 seconds and failure at/after 900. Keep its provenance distinct from unmodified real-issuer headers; rejection at 900 alone cannot prove that later boundary.

Acceptance covers both audiences and their mutual rejection, identity-token-as-bearer rejection, real claims, issuer/audience/algorithm/kid failures, five-phase key rotation, concurrent unknown-kid request counts, freshness boundaries, temporary failures, protocol failures and maximum-age closure. Controlled time replaces multi-minute sleeps where the actual consumer exposes it; validation behavior itself is not replaced.

The same command runs locally and in CI, fails on missing inputs, produces nonzero status for any failed case, and writes zero-skip JUnit plus a machine-readable summary binding source SHAs, exact package versions/hashes, public kids, case counts and HTTP fetch counts. No credentials, tokens, cookies, user data, private keys or private paths enter those public artifacts.

## Publication and delivery

Changes are delivered through independent branches, explicit commits, two-stage review, required PR checks, normal merges and exact-main verification. This preparatory specification does not authorize skipping producer delivery gates.

A changed Platform binary needs a new immutable package version; neither `0.8.0-alpha.2` nor `0.10.1` may be rebuilt or overwritten. The concrete forward release candidate is `0.10.2`, subject to the existing formal workflow's exact-version registration, complete seven-package set, signing, timestamp, feed read-back and protected Environment approval. Code and publication preparation must be reviewable before any required owner action is requested. No unsigned local package is presented as final feed acceptance, and no new image or deployment is part of this work.

After actual fixed-package consumption and cross-repository evidence pass, update all project records with the observed C01 result. C02 and C03 remain subsequent tasks; C04 retains its migration and cutover prerequisites.
