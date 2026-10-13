using CP6.Platform.Contracts.Context;

namespace CP6.Platform.Abstractions.Context;

/// <summary>Each job attempt authenticates its current service and registration; audit requester is not executor.</summary>
public sealed class BackgroundContextExecutorV1<TJob>(
    TrustedContextExecutorV1 executor, IContextSourceVerifierV1<TJob> verifier)
{
    public Task ExecuteAsync(TJob job, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(ContextEntryKindV1.Background, job, verifier, handler, cancellationToken);
}
