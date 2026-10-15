using CP6.Platform.EntityFramework.OutboxV2;

namespace CP6.Platform.EntityFramework.TransportV1;

/// <summary>Narrow opt-in bridge from the existing OutboxV2 dispatcher to a Host-composed transport.
/// The delegate must perform route/producer qualification against the committed Owner send intent;
/// this bridge neither creates that authority nor marks the business operation complete.</summary>
public sealed class TransportOutboxPublisherV1(
    Func<Cp6OutboxDispatchMessage, CancellationToken, ValueTask<OutboxTransportResultV2>> publish) : IOutboxPublisherV2
{
    private readonly Func<Cp6OutboxDispatchMessage, CancellationToken, ValueTask<OutboxTransportResultV2>> transport
        = publish ?? throw new ArgumentNullException(nameof(publish));

    public async ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            if (message is null || message.Payload.IsEmpty || message.Payload.Length > Cp6TransactionalMessagingGuard.MaxPayloadBytes)
                return new(OutboxTransportDecisionV2.DefinitelyRejected, "TRANSPORT_ENVELOPE_SIZE");
            // Do not let a caller's mutable memory change the admitted payload during an asynchronous send.
            var owned = message with { Payload = message.Payload.ToArray() };
            var result = await transport(owned, ct).AsTask().WaitAsync(ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (result is null || !Enum.IsDefined(result.Decision))
                return new(OutboxTransportDecisionV2.Unknown, "TRANSPORT_UNKNOWN");
            return result.Decision switch
            {
                OutboxTransportDecisionV2.Confirmed => new(OutboxTransportDecisionV2.Confirmed),
                OutboxTransportDecisionV2.DefinitelyRejected => new(OutboxTransportDecisionV2.DefinitelyRejected,
                    "TRANSPORT_REJECTED", result.Retryable),
                _ => new(OutboxTransportDecisionV2.Unknown, "TRANSPORT_UNKNOWN")
            };
        }
        catch (Exception) { return new(OutboxTransportDecisionV2.Unknown, "TRANSPORT_UNKNOWN"); }
    }
}
