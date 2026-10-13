using CP6.Platform.Contracts;

namespace CP6.Platform.Abstractions.Context;

public sealed class ContextScopeAccessorV1 : IRequestContextAccessor, ITrustedContextAccessorV1
{
    private readonly AsyncLocal<Frame?> _current = new();

    public IRequestContext? Current => _current.Value?.Value?.Context;
    public TrustedRequestContextV1? CurrentTrusted => _current.Value?.Value?.Trusted;

    /// <summary>Compatibility scope only: a legacy snapshot never produces trusted-binding evidence.</summary>
    public Task<bool> ExecuteLegacyAsync(
        Func<CancellationToken, ValueTask<RequestContextSnapshot?>> resolve,
        Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resolve);
        return ExecuteScopedAsync(async ct =>
        {
            var snapshot = await resolve(ct).ConfigureAwait(false);
            if (snapshot is null) return null;
            try
            {
                return new ScopeValue(new RequestContext(snapshot), null);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }, handler, cancellationToken);
    }

    internal async Task<bool> ExecuteScopedAsync(
        Func<CancellationToken, ValueTask<ScopeValue?>> resolve,
        Func<CancellationToken, Task> handler, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        var previous = _current.Value;
        var frame = new Frame();
        // Mask outer identity during authentication and authority lookup, even across awaits.
        _current.Value = frame;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var resolved = await resolve(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (resolved is null) return false;
            frame.Set(resolved);
            await handler(cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            // Captured ExecutionContexts share the holder, so they also lose access after completion.
            frame.Set(null);
            _current.Value = previous;
        }
    }

    internal sealed record ScopeValue(IRequestContext Context, TrustedRequestContextV1? Trusted);

    private sealed class Frame
    {
        private ScopeValue? _value;
        public ScopeValue? Value => Volatile.Read(ref _value);
        public void Set(ScopeValue? value) => Interlocked.Exchange(ref _value, value);
    }
}
