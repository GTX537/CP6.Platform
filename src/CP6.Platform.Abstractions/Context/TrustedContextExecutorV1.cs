using CP6.Platform.Contracts.Context;

namespace CP6.Platform.Abstractions.Context;

public sealed class TrustedContextExecutorV1
{
    private readonly IContextBindingAuthorityV1 _authority;
    private readonly ContextScopeAccessorV1 _accessor;
    private readonly TimeProvider _timeProvider;

    public TrustedContextExecutorV1(IContextBindingAuthorityV1 authority,
        ContextScopeAccessorV1 accessor, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(accessor);
        _authority = authority;
        _accessor = accessor;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public Task ExecuteAsync<TInput>(ContextEntryKindV1 expectedKind, TInput input,
        IContextSourceVerifierV1<TInput> verifier, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        return _accessor.ExecuteScopedAsync(async ct =>
        {
            var verified = await verifier.VerifyAsync(input, ct).ConfigureAwait(false)
                ?? throw new ContextResolutionExceptionV1(ContextResolutionFailureV1.AuthenticationRequired);
            ct.ThrowIfCancellationRequested();
            var frozen = ContextBindingValidatorV1.FreezeInput(verified, expectedKind);
            var registration = await _authority.ResolveCurrentAsync(frozen.Operation, frozen.Source, ct)
                .ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            var trusted = ContextBindingValidatorV1.Bind(frozen, registration, _timeProvider.GetUtcNow());
            return new ContextScopeAccessorV1.ScopeValue(trusted.Context, trusted);
        }, handler, cancellationToken);
    }
}
