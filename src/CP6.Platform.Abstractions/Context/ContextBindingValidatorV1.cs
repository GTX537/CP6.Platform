using CP6.Platform.Contracts;
using CP6.Platform.Contracts.Context;

namespace CP6.Platform.Abstractions.Context;

internal static class ContextBindingValidatorV1
{
    internal static ContextResolutionInputV1 FreezeInput(ContextResolutionInputV1 input, ContextEntryKindV1 kind)
    {
        Require(input.Operation is not null && input.Source is not null);
        var op = input.Operation!;
        var source = input.Source!;
        Require(Enum.IsDefined(kind) && op.EntryKind == kind && op.ExpectedTenantId != Guid.Empty);
        Require(Metadata(op.HostId, 128) && Metadata(op.ProfileId, 128) && HasValue(op.Audience)
            && Metadata(op.Purpose, 128) && Metadata(op.SourceReference, 256) && HasValue(op.CorrelationId));
        ValidateActor(source.Actor);
        ValidateActor(source.EffectiveActor);
        Require(source.Actor.TenantId == op.ExpectedTenantId && source.EffectiveActor.TenantId == op.ExpectedTenantId
            && source.Actor.Issuer == source.EffectiveActor.Issuer && source.Audience == op.Audience);
        Require(Metadata(source.Region, 128) && Metadata(source.AuthenticationReference, 256)
            && OptionalMetadata(source.SourceIdentityVersion, 128)
            && OptionalMetadata(source.OriginalRequesterReference, 256));
        if (kind is ContextEntryKindV1.Message or ContextEntryKindV1.Background)
            Require(source.Actor.Kind == ContextActorKindV1.Service && source.EffectiveActor.Kind == ContextActorKindV1.Service);
        // A public source cannot impersonate a human/service, or vice versa.
        Require((source.Actor.Kind == ContextActorKindV1.Public) == (source.EffectiveActor.Kind == ContextActorKindV1.Public));
        return new(op with { RequiredScopes = FreezeScopes(op.RequiredScopes) },
            source with { Scopes = FreezeScopes(source.Scopes) });
    }

    internal static TrustedRequestContextV1 Bind(ContextResolutionInputV1 input,
        ContextBindingRegistrationV1? registration, DateTimeOffset now)
    {
        Require(registration is not null);
        var r = registration! with { AllowedScopes = FreezeScopes(registration!.AllowedScopes) };
        var op = input.Operation;
        var source = input.Source;
        Require(r.IsActive && r.ValidFromUtc <= now && now < r.ValidUntilUtc
            && source.AuthenticatedAtUtc <= now && now < source.ExpiresAtUtc
            && source.AuthenticatedAtUtc < source.ExpiresAtUtc);
        Require(r.TenantId == op.ExpectedTenantId && r.OrganizationId == r.TenantId
            && r.HostId == op.HostId && r.ProfileId == op.ProfileId && r.EntryKind == op.EntryKind
            && r.Issuer == source.Actor.Issuer && r.Region == source.Region
            && r.Audience == op.Audience && r.Purpose == op.Purpose
            && r.Actor == source.Actor && r.EffectiveActor == source.EffectiveActor);
        Require(Metadata(r.RegistrationRef, 256) && Metadata(r.RegistrationVersion, 128)
            && OptionalMetadata(r.PermissionDecisionRef, 256));
        Require(source.Actor == source.EffectiveActor || HasValue(r.PermissionDecisionRef));
        var isPublic = source.Actor.Kind == ContextActorKindV1.Public;
        Require(!isPublic || (op.EntryKind == ContextEntryKindV1.Http && r.PublicRouteAllowed));
        var authenticatedScopes = new HashSet<string>(source.Scopes, StringComparer.Ordinal);
        var registeredScopes = new HashSet<string>(r.AllowedScopes, StringComparer.Ordinal);
        Require(op.RequiredScopes.All(scope => authenticatedScopes.Contains(scope) && registeredScopes.Contains(scope)));
        var granted = Array.AsReadOnly(op.RequiredScopes.Distinct(StringComparer.Ordinal).ToArray());
        var context = new RequestContext(new RequestContextSnapshot(r.TenantId, source.EffectiveActor.UserId,
            source.EffectiveActor.Subject, op.Audience, op.CorrelationId, source.TokenId, isPublic));
        var evidence = new ContextResolutionEvidenceV1(Guid.NewGuid(), op.HostId, op.ProfileId,
            r.RegistrationRef, r.RegistrationVersion, r.Issuer, r.TenantId, r.Region,
            source.Actor, source.EffectiveActor, source.AuthenticatedAtUtc, now, op.EntryKind, op.Purpose,
            op.SourceReference, source.AuthenticationReference, granted, source.SourceIdentityVersion,
            source.OriginalRequesterReference, r.PermissionDecisionRef);
        return new(context, evidence);
    }

    private static void ValidateActor(ContextActorV1? actor)
    {
        Require(actor is not null);
        Require(actor!.TenantId != Guid.Empty && HasValue(actor.Subject) && Enum.IsDefined(actor.Kind)
            && IsIssuer(actor.Issuer) && (actor.Kind == ContextActorKindV1.Human || actor.UserId is null));
    }

    private static bool IsIssuer(string? issuer) => HasValue(issuer)
        && issuer == issuer!.Trim()
        && Uri.TryCreate(issuer, UriKind.Absolute, out var uri)
        && uri.IsWellFormedOriginalString()
        && uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(uri.Host)
        && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment)
        && !issuer.Any(char.IsControl);

    private static IReadOnlyCollection<string> FreezeScopes(IReadOnlyCollection<string>? scopes)
    {
        Require(scopes is not null);
        var copy = scopes!.ToArray();
        Require(copy.All(HasValue));
        return Array.AsReadOnly(copy);
    }

    private static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
    private static bool Metadata(string? value, int max) => HasValue(value) && value!.Length <= max;
    private static bool OptionalMetadata(string? value, int max) => value is null || Metadata(value, max);
    private static void Require(bool condition)
    {
        if (!condition) throw new ContextResolutionExceptionV1(ContextResolutionFailureV1.BindingRejected);
    }
}
