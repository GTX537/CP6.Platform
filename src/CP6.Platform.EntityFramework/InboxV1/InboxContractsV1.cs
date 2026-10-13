using System.Security.Cryptography;
using System.Text.Json;

namespace CP6.Platform.EntityFramework.InboxV1;

public enum PreCheckpointIdentityDecisionV1
{
    TransportIdentityConflict, FreshCandidate, TransportDuplicate, OlderVersionIgnored,
    SameVersionEqual, SameVersionConflict, DependencyUnavailable
}

public enum InboxLockStageV1 { Registration, Reconciliation, Published, Projection, Inbox, Checkpoint, Observation }
public sealed record InboxLockV1(InboxLockStageV1 Stage, long Key);

/// <summary>Trusted Owner's existing advisory-lock inventory; never derive a replacement namespace here.</summary>
public sealed record InboxLockPlanV1(string Profile, string WriterSetReference, IReadOnlyList<InboxLockV1> Locks);

/// <summary>Returned only by the trusted host contract/registration adapter, never deserialized from a request.</summary>
public sealed record InboxContractBindingV1(string Owner, string OperationKey, string RequestVersion,
    string InputDomain, string InputCodec, string InputDigest, string EventType, string Source, string Region,
    string SchemaIdentity, string BundleIdentity, string RegistrationReference);

public sealed record InboxAuthorityVersionV1(Guid TenantId, string AggregateId, int Version,
    string EventType, string DataSha256, string SourceReference);

/// <summary>Owner reads its original lineage/projection/observations under the held locks. Incoming is not authority.</summary>
public sealed record InboxAuthoritySnapshotV1(bool Available, InboxAuthorityVersionV1? Current,
    IReadOnlyList<InboxAuthorityVersionV1> History);

public sealed record InboxOwnerResultV1(string NativeReceiptReference, ReadOnlyMemory<byte> OriginalResult);

/// <summary>Trusted in-process Owner adapter; all reads/writes use tx.Context/Transaction. No commit or remote effects.</summary>
public interface ICp6InboxPreCheckpointV1
{
    ValueTask<InboxLockPlanV1> GetLockPlanAsync(InboxTransactionV1 tx, Cp6InboxDelivery delivery,
        InboxTransportV1? original, CancellationToken ct);
    ValueTask<InboxContractBindingV1> ValidateContractAsync(InboxTransactionV1 tx,
        Cp6InboxDelivery delivery, CancellationToken ct);
    ValueTask<InboxAuthoritySnapshotV1> ReadAuthorityAsync(InboxTransactionV1 tx, InboxInputV1 input, CancellationToken ct);
    /// <summary>
    /// Fresh alone may apply new business after the Owner's current final gate. All other decisions record
    /// observations/quarantine only, preserving original business/receipt. Conflict returns empty result bytes
    /// and its immutable native evidence reference. Foreign originals are masked to null: never mutate another tenant.
    /// </summary>
    ValueTask<InboxOwnerResultV1> ApplyDecisionAsync(InboxTransactionV1 tx, InboxInputV1 input,
        PreCheckpointIdentityDecisionV1 decision, InboxTransportV1? original, CancellationToken ct);
}

/// <summary>Current read/field policy only; throw on denial. A new-write grant must not be required.</summary>
public interface IInboxResultReaderV1
{
    ValueTask<ReadOnlyMemory<byte>> AuthorizeAndFilterAsync(InboxTransactionV1 tx,
        InboxDeliveryOutcomeV1 outcome, InboxOriginalResultV1? original, CancellationToken ct);
}

public sealed class InboxProtocolExceptionV1(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Copied original UTF8 bytes and independently calculated raw/data digest domains.</summary>
public sealed class InboxInputV1
{
    private readonly byte[] _raw;
    private readonly byte[] _data;
    internal InboxInputV1(Cp6InboxDelivery delivery, InboxContractBindingV1 binding)
    {
        _raw = delivery.Payload.ToArray();
        _data = OriginalData(_raw);
        Delivery = delivery with { Payload = ReadOnlyMemory<byte>.Empty };
        Binding = binding;
        RawEnvelopeSha256 = Hash(_raw);
        DataPayloadSha256 = Hash(_data);
    }
    // Payload lives only in the separate defensive-copy accessors.
    public Cp6InboxDelivery Delivery { get; }
    public InboxContractBindingV1 Binding { get; }
    public ReadOnlyMemory<byte> OriginalEnvelope => _raw.ToArray();
    public ReadOnlyMemory<byte> OriginalDataPayload => _data.ToArray();
    public string RawEnvelopeSha256 { get; }
    public string DataPayloadSha256 { get; }
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static byte[] OriginalData(byte[] bytes)
    {
        try
        {
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions { MaxDepth = 64 });
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw new JsonException();
            var names = new Stack<HashSet<string>>();
            names.Push(new(StringComparer.Ordinal));
            byte[]? data = null;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
                if (reader.TokenType == JsonTokenType.EndObject) names.Pop();
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var name = reader.GetString()!;
                if (!names.Peek().Add(name)) throw new JsonException();
                if (reader.CurrentDepth != 1 || name != "data") continue;
                if (!reader.Read()) throw new JsonException();
                var start = checked((int)reader.TokenStartIndex);
                // A copy skips the value only to locate its exact byte boundary. The main reader
                // still traverses every nested object to reject duplicate (including escaped) names.
                var end = reader;
                end.Skip();
                data = bytes.AsSpan(start, checked((int)end.BytesConsumed) - start).ToArray();
                if (reader.TokenType == JsonTokenType.StartObject) names.Push(new(StringComparer.Ordinal));
            }
            if (data is null || names.Count != 0) throw new JsonException();
            return data;
        }
        catch (JsonException) { throw new InboxProtocolExceptionV1("CP6_INBOX_ORIGINAL_JSON_INVALID"); }
    }
}

/// <summary>Only a pending database write. Contains no result bytes and no transport-success mapping.</summary>
public sealed class PendingCommitResult
{
    internal PendingCommitResult(Guid receiptId, string operationKey, PreCheckpointIdentityDecisionV1 decision)
        => (ReceiptId, OperationKey, Decision) = (receiptId, operationKey, decision);
    public Guid ReceiptId { get; }
    public string OperationKey { get; }
    public PreCheckpointIdentityDecisionV1 Decision { get; }
    public string EnvelopeContractVersion => "inbox-caller-transaction-v1";
}

/// <summary>Minted only after a committed receipt is visible in a caller-owned read-only transaction and current read authorization.</summary>
public sealed class CommittedDeliveryOutcome
{
    private readonly byte[] _result;
    internal CommittedDeliveryOutcome(InboxDeliveryOutcomeV1 outcome, InboxOriginalResultV1? original, ReadOnlyMemory<byte> filtered)
    {
        ReceiptId = outcome.Id;
        Decision = outcome.Decision;
        OriginalResultSha256 = original?.ResultSha256;
        _result = filtered.ToArray();
    }
    public Guid ReceiptId { get; }
    public PreCheckpointIdentityDecisionV1 Decision { get; }
    public string EnvelopeContractVersion => "inbox-caller-transaction-v1";
    public string? OriginalResultSha256 { get; }
    public ReadOnlyMemory<byte> VisibleResult => _result.ToArray();
    public string TransportOutcome => Decision switch
    {
        PreCheckpointIdentityDecisionV1.TransportIdentityConflict or PreCheckpointIdentityDecisionV1.SameVersionConflict => "DROP",
        PreCheckpointIdentityDecisionV1.DependencyUnavailable => "RETRY",
        _ => "SUCCESS"
    };
}

/// <summary>Historical original-operation read. Intentionally cannot acknowledge any current transport delivery.</summary>
public sealed class CommittedOriginalResultV1
{
    private readonly byte[] _visible;
    internal CommittedOriginalResultV1(InboxOriginalResultV1 original, ReadOnlyMemory<byte> visible)
    {
        OperationKey = original.OperationKey;
        OriginalResultSha256 = original.ResultSha256;
        _visible = visible.ToArray();
    }
    public string OperationKey { get; }
    public string OriginalResultSha256 { get; }
    public ReadOnlyMemory<byte> VisibleResult => _visible.ToArray();
}

internal static class InboxContentDecisionV1
{
    internal static PreCheckpointIdentityDecisionV1 Evaluate(InboxInputV1 input, InboxTransportV1? original,
        InboxAuthoritySnapshotV1 authority)
    {
        if (original is not null && !original.Matches(input)) return PreCheckpointIdentityDecisionV1.TransportIdentityConflict;
        if (!authority.Available) return PreCheckpointIdentityDecisionV1.DependencyUnavailable;
        var versions = authority.History.Concat(authority.Current is null ? [] : new[] { authority.Current }).ToArray();
        if (versions.Any(v => v.TenantId != input.Delivery.TenantId || v.AggregateId != input.Delivery.AggregateId ||
            v.Version < 1 || string.IsNullOrWhiteSpace(v.SourceReference) || !InboxValidationV1.IsHash(v.DataSha256)))
            return PreCheckpointIdentityDecisionV1.DependencyUnavailable;
        if (versions.Any(v => v.Version == input.Delivery.AggregateVersion &&
            (v.EventType != input.Binding.EventType || v.DataSha256 != input.DataPayloadSha256)))
            return PreCheckpointIdentityDecisionV1.SameVersionConflict;
        if (original is not null) return PreCheckpointIdentityDecisionV1.TransportDuplicate;
        if (authority.Current is null || input.Delivery.AggregateVersion > authority.Current.Version)
            return PreCheckpointIdentityDecisionV1.FreshCandidate;
        return input.Delivery.AggregateVersion < authority.Current.Version
            ? PreCheckpointIdentityDecisionV1.OlderVersionIgnored : PreCheckpointIdentityDecisionV1.SameVersionEqual;
    }
}

internal static class InboxValidationV1
{
    internal static bool IsHash(string? value) => value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    internal static void Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw new InboxProtocolExceptionV1("CP6_INBOX_BINDING_INVALID");
    }
}
