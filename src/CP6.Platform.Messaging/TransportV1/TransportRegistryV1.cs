using System.Security.Cryptography;
using System.Text.Json;
using CP6.Platform.Messaging.NativeContracts;

namespace CP6.Platform.Messaging.TransportV1;

public sealed class TransportQualificationV1
{
    internal TransportQualificationV1(bool qualified, VerifiedTransportMessageV1? message, string? diagnostic)
    { IsQualified = qualified; Message = message; Diagnostics = diagnostic is null ? [] : Array.AsReadOnly(new[] { diagnostic }); }
    public bool IsQualified { get; }
    public VerifiedTransportMessageV1? Message { get; }
    public IReadOnlyList<string> Diagnostics { get; } = [];
}

public sealed class VerifiedTransportMessageV1
{
    private readonly byte[] raw;
    internal VerifiedTransportMessageV1(object scope, TransportRouteV1 route, TransportPurposeV1 purpose,
        AuthenticatedTransportFactsV1 facts, byte[] ownedRaw, bool contractValid)
    { Scope = scope; Route = route; Purpose = purpose; Facts = facts; raw = ownedRaw.ToArray(); ContractValid = contractValid; }
    internal object Scope { get; }
    internal bool ContractValid { get; }
    public TransportRouteV1 Route { get; } = null!;
    public TransportPurposeV1 Purpose { get; }
    public AuthenticatedTransportFactsV1 Facts { get; } = null!;
    public string RawEnvelopeSha256 => Facts.RawEnvelopeSha256;
    public byte[] CopyRawBytes() => raw.ToArray();
}

public sealed class QuarantineRecordV1
{
    private readonly byte[] raw;
    public QuarantineRecordV1(string profile, string route, string policy, string messageId,
        string rawEnvelopeSha256, ReadOnlyMemory<byte> rawBytes, AuthenticatedTransportFactsV1? nativeFacts = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile); ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(policy); ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rawEnvelopeSha256);
        if (rawBytes.Length is < 1 or > TransportRouteV1.GenericMaxBytes) throw new ArgumentOutOfRangeException(nameof(rawBytes));
        Profile = profile; Route = route; Policy = policy; MessageId = messageId;
        RawEnvelopeSha256 = rawEnvelopeSha256; raw = rawBytes.ToArray(); NativeFacts = nativeFacts;
    }
    public string Profile { get; } = "";
    public string Route { get; } = "";
    public string Policy { get; } = "";
    public string MessageId { get; } = "";
    public string RawEnvelopeSha256 { get; } = "";
    public AuthenticatedTransportFactsV1? NativeFacts { get; }
    public byte[] CopyRawBytes() => raw.ToArray();
}

public sealed class TransportRegistryV1
{
    private readonly Dictionary<(string Profile, string Route, string Type, string Schema, string Version), TransportRouteV1> routes = [];
    private readonly Dictionary<(string Profile, string Route), int> limits = [];
    private readonly Dictionary<(string Profile, string Route), TransportRouteV1> policies = [];
    private readonly ITransportAuthorityV1 authority;
    private readonly object scope = new();

    public TransportRegistryV1(IEnumerable<TransportRouteV1> routes, int hostMaxBytes, int brokerMaxBytes,
        ITransportAuthorityV1 authority)
    {
        ArgumentNullException.ThrowIfNull(routes); ArgumentNullException.ThrowIfNull(authority);
        if (hostMaxBytes < 1 || brokerMaxBytes < 1) throw new ArgumentOutOfRangeException(nameof(hostMaxBytes));
        this.authority = authority;
        foreach (var item in routes)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!this.routes.TryAdd((item.Profile, item.Route, item.EventType, item.SchemaId, item.SchemaVersion), item))
                throw new ArgumentException("Duplicate transport registration.", nameof(routes));
            var limit = Math.Min(item.MaxBytes, Math.Min(hostMaxBytes, brokerMaxBytes));
            var key = (item.Profile, item.Route);
            if (policies.TryGetValue(key, out var previous) &&
                (previous.Producer != item.Producer || previous.Source != item.Source || previous.Topic != item.Topic || previous.MaxBytes != item.MaxBytes))
                throw new ArgumentException("A Host route must have one unambiguous transport policy.", nameof(routes));
            policies.TryAdd(key, item);
            limits[key] = limit;
        }
        if (this.routes.Count == 0) throw new ArgumentException("At least one explicit route is required.", nameof(routes));
    }

    /// <summary>The Host selects profile/route from immutable endpoint composition, never payload/header.
    /// A returned Message on failure is an authenticated quarantine capability only. IsQualified is
    /// required before further admission; it never proves business commit, Inbox admission or ACK.</summary>
    public async ValueTask<TransportQualificationV1> QualifyAsync(string profile, string route,
        ReadOnlyMemory<byte> rawBytes, object hostEvidence, TransportPurposeV1 purpose,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(purpose) || hostEvidence is null ||
            !limits.TryGetValue((profile, route), out var limit)) return Reject("ROUTE_UNAVAILABLE");
        if (rawBytes.IsEmpty || rawBytes.Length > limit) return Reject("ENVELOPE_SIZE");
        var owned = rawBytes.ToArray();
        VerifiedTransportMessageV1? quarantine = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // This fixed Host route policy authenticates native evidence before parsing the untrusted
            // contract tuple. Unknown contracts can be durably quarantined without schema approval.
            var selected = policies[(profile, route)];
            var hash = Hash(owned);
            var facts = await authority.AuthenticateAsync(new(selected, purpose, hash, owned.Length), hostEvidence, cancellationToken)
                .AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (facts is null || facts.Producer != selected.Producer || facts.Topic != selected.Topic ||
                facts.RawEnvelopeSha256 != hash || string.IsNullOrWhiteSpace(facts.NativeReference) ||
                facts.NativeReference.Length > 512 || facts.ContentType != Cp6CloudEventCodec.StructuredContentType ||
                facts.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(facts.Region) ||
                string.IsNullOrWhiteSpace(facts.MessageId) || facts.MessageId.Length > 128 ||
                string.IsNullOrWhiteSpace(facts.AggregateId) || facts.AggregateId.Length > 128 || facts.AggregateVersion < 1 ||
                string.IsNullOrWhiteSpace(facts.PartitionKey) || facts.PartitionKey.Length > 512)
                return Reject("AUTHORITY_REJECTED");
            if (purpose == TransportPurposeV1.Receive)
                quarantine = new(scope, selected, purpose, facts, owned, false);
            using var document = Cp6NativeJson.ParseCloudEvent(owned);
            var root = document.RootElement;
            if (!routes.TryGetValue((profile, route, Get(root, "type"), Get(root, "dataschema"), Get(root, "schemaversion")), out selected))
                return new(false, quarantine, "CONTRACT_UNAVAILABLE");
            var valid = selected.Validator.ValidateForTransport(owned).IsValid &&
                facts.PartitionKey == Cp6DaprKafkaConventions.GetPartitionKey(facts.TenantId, facts.AggregateId) &&
                Get(root, "source") == selected.Source && Get(root, "id") == facts.MessageId &&
                Get(root, "tenantid") == facts.TenantId.ToString("D") && Get(root, "region") == facts.Region &&
                Get(root, "aggregateid") == facts.AggregateId &&
                root.TryGetProperty("aggregateversion", out var version) && version.TryGetInt32(out var number) && number == facts.AggregateVersion &&
                Get(root, "correlationid") == facts.CorrelationId && Get(root, "causationid") == facts.CausationId;
            var token = new VerifiedTransportMessageV1(scope, selected, purpose, facts, owned, valid);
            return new(valid, token, valid ? null : "ENVELOPE_REJECTED");
        }
        catch (JsonException) { return new(false, quarantine, "MALFORMED_JSON"); }
        catch (OperationCanceledException) { return Reject("AUTHORITY_UNAVAILABLE"); }
        catch (Exception) { return new(false, quarantine, "QUALIFICATION_UNAVAILABLE"); }
    }

    public async ValueTask<TransportDecisionV1> PublishAsync(VerifiedTransportMessageV1 message,
        IMessageTransportV1 transport, CancellationToken cancellationToken = default)
    {
        if (message is null || !ReferenceEquals(message.Scope, scope) || !message.ContractValid ||
            message.Purpose != TransportPurposeV1.Publish || transport is null) return TransportDecisionV1.DefinitelyRejected;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var decision = await transport.PublishAsync(message, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return Enum.IsDefined(decision) ? decision : TransportDecisionV1.Unknown;
        }
        catch (Exception) { return TransportDecisionV1.Unknown; }
    }

    public async ValueTask<QuarantineDecisionV1> QuarantineAsync(VerifiedTransportMessageV1 message,
        string policy, ITransportQuarantineStoreV1 store, CancellationToken cancellationToken = default)
    {
        if (message is null || !ReferenceEquals(message.Scope, scope) || message.Purpose != TransportPurposeV1.Receive ||
            store is null || string.IsNullOrWhiteSpace(policy) || policy.Length > 128 || policy.Any(char.IsControl))
            return QuarantineDecisionV1.Retry;
        var original = new QuarantineRecordV1(message.Route.Profile, message.Route.Route, policy,
            message.Facts.MessageId, message.RawEnvelopeSha256, message.CopyRawBytes(), message.Facts);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await store.PersistAsync(original, cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            var saved = await store.ReadCommittedAsync(original.Profile, original.Route, original.Policy,
                original.MessageId, original.RawEnvelopeSha256, original.NativeFacts!.NativeReference,
                cancellationToken).AsTask().WaitAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (saved is null || saved.Profile != original.Profile || saved.Route != original.Route || saved.Policy != original.Policy ||
                saved.MessageId != original.MessageId || saved.RawEnvelopeSha256 != original.RawEnvelopeSha256 ||
                saved.NativeFacts is null || saved.NativeFacts != original.NativeFacts)
                return QuarantineDecisionV1.Retry;
            var restored = saved.CopyRawBytes();
            return Hash(restored) == original.RawEnvelopeSha256 && restored.AsSpan().SequenceEqual(original.CopyRawBytes())
                ? QuarantineDecisionV1.Drop : QuarantineDecisionV1.Retry;
        }
        catch (Exception) { return QuarantineDecisionV1.Retry; }
    }

    private static TransportQualificationV1 Reject(string code) => new(false, null, code);
    private static string Get(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
    private static string Hash(byte[] raw) => Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
}
