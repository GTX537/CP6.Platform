using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;

namespace CP6.Platform.Messaging.Context;

/// <summary>
/// Establishes one authenticated message processing scope. The host verifier must validate the actual
/// transport/producer and the existing message contract, including its tenant. Trace/context alone is not proof.
/// Each retry re-verifies; native delivery bytes and CloudEvent contracts are unchanged.
/// </summary>
public sealed class MessageContextExecutorV1<TDelivery>(
    TrustedContextExecutorV1 executor, IContextSourceVerifierV1<TDelivery> verifier)
{
    public Task ExecuteAsync(TDelivery delivery, Func<CancellationToken, Task> handler,
        CancellationToken cancellationToken = default) =>
        executor.ExecuteAsync(ContextEntryKindV1.Message, delivery, verifier, handler, cancellationToken);
}
