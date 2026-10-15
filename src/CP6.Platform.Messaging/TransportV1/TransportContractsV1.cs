namespace CP6.Platform.Messaging.TransportV1;

public enum TransportPurposeV1 { Publish, Receive }
public enum TransportDecisionV1 { Confirmed, DefinitelyRejected, Unknown }
public enum QuarantineDecisionV1 { Retry, Drop }

/// <summary>Host authentication boundary. Implementations must independently authenticate the producer,
/// route entitlement, tenant and region, and actual native transport metadata. Request bytes/headers are
/// claims, never evidence. Publish requires a committed Owner send authorization. Receive requires
/// actual broker delivery evidence. Returning facts is an assertion by this trusted Host implementation,
/// not proof furnished by this library. Never bind an implementation from request-controlled input.</summary>
public interface ITransportAuthorityV1
{
    ValueTask<AuthenticatedTransportFactsV1?> AuthenticateAsync(TransportAuthenticationRequestV1 request,
        object hostEvidence, CancellationToken cancellationToken);
}

public sealed record AuthenticatedTransportFactsV1(string Producer, Guid TenantId, string Region,
    string Topic, string PartitionKey, string MessageId, string AggregateId, int AggregateVersion,
    string CorrelationId, string CausationId, string NativeReference, string RawEnvelopeSha256,
    string ContentType);

public sealed record TransportAuthenticationRequestV1(TransportRouteV1 Route, TransportPurposeV1 Purpose,
    string RawEnvelopeSha256, int ByteLength);

/// <summary>Transport result Confirmed means only that the actual transport accepted the send.</summary>
public interface IMessageTransportV1
{
    ValueTask<TransportDecisionV1> PublishAsync(VerifiedTransportMessageV1 message, CancellationToken cancellationToken);
}

/// <summary>The Host must commit to its durable quarantine policy, then read independently from a
/// committed database transaction. An in-memory implementation or synthesized readback cannot prove
/// durable completion. Implementations must preserve original bytes and all identity fields.
/// NativeReference is part of the durable lookup identity: one body/id may arrive at distinct broker offsets.</summary>
public interface ITransportQuarantineStoreV1
{
    ValueTask PersistAsync(QuarantineRecordV1 original, CancellationToken cancellationToken);
    ValueTask<QuarantineRecordV1?> ReadCommittedAsync(string profile, string route, string policy,
        string messageId, string rawEnvelopeSha256, string nativeReference, CancellationToken cancellationToken);
}
