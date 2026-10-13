namespace CP6.Platform.Contracts.Context;

public enum ContextEntryKindV1 { Http, Message, Background }
public enum ContextActorKindV1 { Human, Service, Public }

public sealed record ContextActorV1(
    Guid TenantId, string Issuer, string Subject, ContextActorKindV1 Kind, Guid? UserId);

/// <summary>Requested operation and addressing facts; these are not authentication proof.</summary>
public sealed record ContextOperationV1(
    ContextEntryKindV1 EntryKind, string HostId, string ProfileId, string Audience,
    string Purpose, Guid ExpectedTenantId, string SourceReference, string CorrelationId,
    IReadOnlyCollection<string> RequiredScopes);

/// <summary>Only a host-owned verifier may attest these facts. Never deserialize a caller's claim as proof.</summary>
public sealed record AuthenticatedContextSourceV1(
    ContextActorV1 Actor, ContextActorV1 EffectiveActor, string Audience, string Region,
    DateTimeOffset AuthenticatedAtUtc, DateTimeOffset ExpiresAtUtc,
    string AuthenticationReference, string? TokenId, IReadOnlyCollection<string> Scopes,
    string? SourceIdentityVersion = null, string? OriginalRequesterReference = null);

public sealed record ContextResolutionInputV1(
    ContextOperationV1 Operation, AuthenticatedContextSourceV1 Source);

/// <summary>Current authority decision for this exact verified source and operation, including revocation.</summary>
public sealed record ContextBindingRegistrationV1(
    Guid TenantId, Guid OrganizationId, string HostId, string ProfileId, string Issuer,
    string Region, string Audience, string Purpose, ContextEntryKindV1 EntryKind,
    ContextActorV1 Actor, ContextActorV1 EffectiveActor, string RegistrationRef,
    string RegistrationVersion, DateTimeOffset ValidFromUtc, DateTimeOffset ValidUntilUtc,
    bool IsActive, IReadOnlyCollection<string> AllowedScopes,
    bool PublicRouteAllowed = false, string? PermissionDecisionRef = null);

/// <summary>Audit evidence for one resolution; it is not a token or a future business-write permit.</summary>
public sealed record ContextResolutionEvidenceV1(
    Guid ResolutionId, string HostId, string ProfileId, string RegistrationRef,
    string RegistrationVersion, string Issuer, Guid TenantId, string Region,
    ContextActorV1 Actor, ContextActorV1 EffectiveActor, DateTimeOffset AuthenticatedAtUtc,
    DateTimeOffset VerifiedAtUtc, ContextEntryKindV1 EntryKind, string Purpose,
    string SourceReference, string AuthenticationReference, IReadOnlyCollection<string> GrantedScopes,
    string? SourceIdentityVersion, string? OriginalRequesterReference, string? PermissionDecisionRef)
{
    public string ContextContractVersion => "ctx-v1";
    public string CapabilityVersion => "ctx-binding-v1";
}
