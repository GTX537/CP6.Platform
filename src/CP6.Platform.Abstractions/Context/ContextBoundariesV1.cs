using CP6.Platform.Contracts.Context;

namespace CP6.Platform.Abstractions.Context;

/// <summary>
/// Host-owned authentication boundary. Validate the real principal/transport/job source and existing
/// profile before returning facts. Body, headers, trace and serialized evidence cannot authenticate themselves.
/// Called afresh on every execution, including retries. Null means no authenticated source.
/// </summary>
public interface IContextSourceVerifierV1<in TInput>
{
    ValueTask<ContextResolutionInputV1?> VerifyAsync(TInput input, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves CURRENT tenant/issuer/actor registration and revocation for the exact verified source and
/// operation. PublicRouteAllowed must cover that operation's route. Never trust caller-supplied registration,
/// original requester or previously issued evidence. Unavailable authority must fail closed.
/// This does not replace an Owner's same-transaction final business authorization gate.
/// </summary>
public interface IContextBindingAuthorityV1
{
    ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(
        ContextOperationV1 operation, AuthenticatedContextSourceV1 source,
        CancellationToken cancellationToken = default);
}

public interface ITrustedContextAccessorV1
{
    TrustedRequestContextV1? CurrentTrusted { get; }
}

/// <summary>Constructed only by a successful resolution. Consumers cannot mint or reactivate one.</summary>
public sealed class TrustedRequestContextV1
{
    internal TrustedRequestContextV1(IRequestContext context, ContextResolutionEvidenceV1 evidence)
    {
        Context = context;
        Evidence = evidence;
    }

    public IRequestContext Context { get; }
    public ContextResolutionEvidenceV1 Evidence { get; }
}

public enum ContextResolutionFailureV1 { AuthenticationRequired, BindingRejected, AuthorityUnavailable }

/// <summary>Deliberately fixed, non-sensitive messages: no raw identity, registration or credential data.</summary>
public sealed class ContextResolutionExceptionV1(ContextResolutionFailureV1 failure)
    : Exception(failure switch
    {
        ContextResolutionFailureV1.AuthenticationRequired => "Context authentication is required.",
        ContextResolutionFailureV1.AuthorityUnavailable => "Context authority is unavailable.",
        _ => "Context binding was rejected."
    })
{
    public ContextResolutionFailureV1 Failure { get; } = failure;
}
