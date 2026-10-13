# WP02-01 trusted context implementation plan

> Execution: inline, using executing-plans. The assigned READY/CONFIRMED window authorizes implementation. One task-level review follows implementation and local validation; shared status and integration belong to COORD.

**Goal:** Establish and revoke HTTP, message and background context from verified identity plus current authoritative registration, without changing the original seven-field contract.

**Architecture:** A host-owned verifier authenticates each entry. A separate authority resolves its current tenant/issuer/actor/operation binding. A callback-scoped executor validates both and exposes immutable context with independent `ctx-binding-v1` evidence. The legacy HTTP adapter keeps its resolver and missing/invalid snapshot 403 behavior.

**Tech stack:** Existing .NET 8 projects/dependencies; no database, package publication, JWT or CloudEvent changes.

## Frozen inputs and boundaries

- Platform baseline: `dc4c0ab216974339ab3c2cebaaf4bb92c9e9b351` (HEAD and fetched origin/main). Local shared main remains `30bd23af6808d217a23878bd9437513043c52834`; it is not this worktree's baseline.
- Main source pin: `e6ccc89b3e62c7ed2c3d4739757b6ae9f4490c47`; assignment at `e20740e37e20bc0210e0b32c5670c2bce962002a`.
- Source manifest and all 28 AC / PGT-004 responsibility mappings will remain in this evidence directory. Original global acceptance remains NOT_RUN unless actual owner/host evidence establishes it.
- Exact allowed files are the wave4 WP02-01 card. New public types below live exclusively under each project's `Context/` directory and namespace `CP6.Platform.{project}.Context`.
- Original Snapshot, IRequestContext, IRequestContextResolver, auth validators, CloudEvent contracts, EF, project/dependency/lock files and workflows are read-only. No required issuer/region/epoch is added to the old contract. No new storage or migration.

## Public extension signatures (frozen before source editing)

Contracts, `src/CP6.Platform.Contracts/Context/ContextContractsV1.cs`:

```csharp
enum ContextEntryKindV1 { Http, Message, Background }
enum ContextActorKindV1 { Human, Service, Public }
record ContextActorV1(Guid TenantId, string Issuer, string Subject,
    ContextActorKindV1 Kind, Guid? UserId);
record ContextOperationV1(ContextEntryKindV1 EntryKind, string HostId,
    string ProfileId, string Audience, string Purpose, Guid ExpectedTenantId,
    string SourceReference, string CorrelationId, IReadOnlyCollection<string> RequiredScopes);
record AuthenticatedContextSourceV1(ContextActorV1 Actor, ContextActorV1 EffectiveActor,
    string Audience, string Region, DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset ExpiresAtUtc, string AuthenticationReference, string? TokenId,
    IReadOnlyCollection<string> Scopes, string? SourceIdentityVersion = null,
    string? OriginalRequesterReference = null);
record ContextResolutionInputV1(ContextOperationV1 Operation, AuthenticatedContextSourceV1 Source);
record ContextBindingRegistrationV1(Guid TenantId, Guid OrganizationId,
    string HostId, string ProfileId, string Issuer, string Region,
    string Audience, string Purpose, ContextEntryKindV1 EntryKind,
    ContextActorV1 Actor, ContextActorV1 EffectiveActor, string RegistrationRef,
    string RegistrationVersion, DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc,
    bool IsActive, IReadOnlyCollection<string> AllowedScopes,
    bool PublicRouteAllowed = false, string? PermissionDecisionRef = null);
```

`ContextResolutionEvidenceV1` is an immutable record with resolution GUID, host/profile/registration reference/version, issuer/tenant/region, actor/effective actor, authentication/verification timestamps, entry kind/purpose/source/authentication reference, granted scopes, optional source identity version/original requester/permission decision reference. It reports `ContextContractVersion = "ctx-v1"` and `CapabilityVersion = "ctx-binding-v1"`. Evidence is audit data, never an input authorization token.

Abstractions, `src/CP6.Platform.Abstractions/Context/`:

```csharp
interface IContextSourceVerifierV1<in TInput> {
    ValueTask<ContextResolutionInputV1?> VerifyAsync(TInput input, CancellationToken cancellationToken = default);
}
interface IContextBindingAuthorityV1 {
    ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
        AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default);
}
interface ITrustedContextAccessorV1 { TrustedRequestContextV1? CurrentTrusted { get; } }
// TrustedRequestContextV1 has an INTERNAL constructor and read-only Context/Evidence.
// No public API accepts an existing trusted result or evidence to establish a scope.
sealed class ContextScopeAccessorV1 : IRequestContextAccessor, ITrustedContextAccessorV1 {
    IRequestContext? Current { get; }
    TrustedRequestContextV1? CurrentTrusted { get; }
    Task<bool> ExecuteLegacyAsync(Func<CancellationToken, ValueTask<RequestContextSnapshot?>> resolve,
        Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default);
}
sealed class TrustedContextExecutorV1 {
    TrustedContextExecutorV1(IContextBindingAuthorityV1 authority,
        ContextScopeAccessorV1 accessor, TimeProvider? timeProvider = null);
    Task ExecuteAsync<TInput>(ContextEntryKindV1 expectedKind, TInput input,
        IContextSourceVerifierV1<TInput> verifier, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}
sealed class BackgroundContextExecutorV1<TJob> {
    BackgroundContextExecutorV1(TrustedContextExecutorV1 executor, IContextSourceVerifierV1<TJob> verifier);
    Task ExecuteAsync(TJob job, Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default);
}
enum ContextResolutionFailureV1 { AuthenticationRequired, BindingRejected, AuthorityUnavailable }
sealed class ContextResolutionExceptionV1 : Exception {
    ContextResolutionExceptionV1(ContextResolutionFailureV1 failure);
    ContextResolutionFailureV1 Failure { get; }
}
```

AspNetCore, `Context/TrustedRequestContextExtensionsV1.cs` and `Context/TrustedRequestContextMiddlewareV1.cs`:

```csharp
IServiceCollection AddCp6TrustedRequestContextV1<TVerifier,TAuthority>(this IServiceCollection services)
    where TVerifier : class, IContextSourceVerifierV1<HttpContext>
    where TAuthority : class, IContextBindingAuthorityV1;
IApplicationBuilder UseCp6TrustedRequestContextV1(this IApplicationBuilder application);
// Middleware constructor RequestDelegate; InvokeAsync takes HttpContext,
// TrustedContextExecutorV1 and IContextSourceVerifierV1<HttpContext> from scoped DI.
```

Messaging, `Context/MessageContextExecutorV1.cs`: `MessageContextExecutorV1<TDelivery>` has the same constructor and ExecuteAsync shape as the background wrapper and fixes entry kind to Message. No dependency on AspNetCore and no reinterpretation of event bytes or trace as authentication.

## Binding and lifetime decisions

1. Verifier is a trusted host registration, not a caller-provided deserializer. It must use authenticated HTTP principal/profile/session or verified transport/job source. Null means authentication required. Body/header/trace values are never sufficient proof.
2. Authority must resolve current registration/revocation using the verified identity and requested operation on EVERY attempt; neither old evidence nor captured original requester is a substitute. Null/inactive binding rejects. A typed unavailable result fails closed.
3. Exact ordinal tenant/organization, issuer, actor, effective actor, host/profile, region, audience, purpose and entry-kind binding. Issuer is an absolute HTTPS identifier with no userinfo/query/fragment. No normalized issuer equivalence. New registration metadata obeys original evidence lengths. Existing subject/audience/correlation strings preserve their original contract.
4. Both identities belong to the same registered tenant/issuer. Message/background require service actor AND service effective actor. Non-human identity has no fabricated UserId. Explicit delegation needs an authority decision reference. Public identity is HTTP-only and requires explicit registration permission; IsPublic never implies admin.
5. Required scopes must be present in both authenticated source and current registration; exposed scopes are only those requested (empty means no granted scope). Source, operation and registration collections are copied into read-only arrays before reuse. Authentication and registration validity are checked after asynchronous authority resolution.
6. A new empty frame masks any outer context before resolution. Each callback frame is held by an instance AsyncLocal. Finally revokes its shared holder (including captured child ExecutionContexts) and restores the caller's prior frame. Nested and overlapping asynchronous operations remain isolated. No detached operation may retain active authority; later jobs must re-enter through the verifier and authority.
7. Legacy execution yields only the seven-field context and no trusted evidence. Null/invalid snapshot remains 403. The opt-in trusted middleware maps only pre-handler typed resolution failures to 401/403/503; business exceptions and cancellation propagate. Tenantless health uses a host branch outside business context and does not invent a tenant.

## Execution and validation

- [x] Publish START with fixed inputs and authorized paths in the task-specific Handoff branch.
- [x] Add new contract types and compilable failing executor seams, then tests in `tests/CP6.Platform.UnitTests/Context/TrustedContextTests.cs`, `ContextScopeTests.cs`, and `ContextEntryTests.cs`; preserve the discovered, failing assertions in a RED log/TRX before implementation.
- [x] Implement bindings/lifetime and message/background wrappers. Use deterministic task barriers for concurrent/nested/captured-child tests; no timing sleeps.
- [x] Add opt-in HTTP adapter and route existing middleware through legacy explicit scope. Extend only `RequestContextMiddlewareTests.cs` and new `Context/TrustedContextMiddlewareTests.cs`.
- [x] Verify valid binding and exact old seven fields; reject missing/empty tenant, wrong tenant issuer/actor/effective actor, audience/purpose/region, disabled/expired/revoked/current-unavailable bindings, wrong entry kind, insufficient scopes and implicit impersonation/public identity. Verify every retry obtains fresh service identity and registration.
- [x] Verify success, verifier/authority/handler exception, cancellation during resolution/handler, nested A/B/A, serial A/B/A, concurrent asynchronous A/B, captured child cleanup, legacy isolation, HTTP status compatibility and health bypass. Test fixtures prove library behavior only.
- [x] Run the two complete assigned test projects in Release, retain actual commands, SDK, input/source/dependency digests, discovered/pass/fail/skip counts and artifacts. No zero-discovery or skipped result counts as acceptance.

```powershell
C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.AspNetCoreTests/CP6.Platform.AspNetCoreTests.csproj --configuration Release
C:/Users/tt/.dotnet/dotnet.exe test tests/CP6.Platform.UnitTests/CP6.Platform.UnitTests.csproj --configuration Release
```

- [ ] Write `docs/WP02-01-TRUSTED-CONTEXT.md`, append compatibility/adoption guidance to `docs/P02-REQUEST-CONTEXT.md`, and evidence `ACCEPTANCE.md`, `PROVENANCE.json`, `VALIDATION.md`, `DELIVERY.md`. Preserve all 28 original AC and PGT-004 as mapped obligations; report Main/CRM/PG/final-gate/real-host adoption separately and honestly.
- [ ] Perform one concentrated review against latest confirmed main. Fix material issues and run affected regression only. Explicitly stage task files and create auditable commits.
- [ ] Before candidate/Handoff push inspect remote protected branch and candidate workflows/events/dependencies. Publish no Actions runs, packages, database changes or deployment. Deliver candidate SHA and fixed evidence to COORD; final merge/status/main containment remains the coordinator's window.

## Plan self-review

Every task-card obligation is addressed above. No change to original seven fields, signature/JWT validation, event bytes or DB/final business authorization. Zero fixed S01 vectors are directly assigned. The current code uses a scoped accessor and finally cleanup: concurrency tests specify the NEW explicit-scope extension, not a claim of a previously observed AsyncLocal defect. Optional telemetry identifiers must be opaque and contain no tokens/secrets. Actual Main/CRM authentication adapters and live PostgreSQL connection-count evidence are outside this Platform write window and remain NOT_RUN.

Async-flow reference: [.NET ExecutionContext explanation](https://devblogs.microsoft.com/dotnet/how-async-await-really-works/); correctness is additionally exercised with local callback, nested, parallel and captured-child tests.
