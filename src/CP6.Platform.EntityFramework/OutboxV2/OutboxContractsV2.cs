using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.OutboxV2;

public enum OutboxPhaseV2 { Active, Draining, Blocked, Retired }
public enum OutboxDecisionV2 { PendingCommit, Committed, OutcomeUnknown, RouteUnavailable, RouteEpochChanged, LeaseLost, Conflict, Expired, Retained, PolicyUnavailable }
public enum OutboxIntentV2 { PublishConfirmed, RecordPublishFailure }
public enum OutboxAccessV2 { ReadReceipt, Claim, Complete, Renew, Switch, Retention, Send }
public sealed record OutboxRootKeyV2(string OwnerId, Guid TenantId, string QueueDomainId, string RouteId)
{
    public string Id => OutboxEncodingV2.Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new[] { OwnerId, TenantId.ToString("D"), QueueDomainId, RouteId })));
}
public sealed record OutboxPolicyV2(string Version, int BatchLimit, int MaxAttempts, TimeSpan LeaseDuration,
    TimeSpan RetryDelay, TimeSpan MaximumRetryDelay, TimeSpan PublishBudget, TimeSpan MaximumClaimLifetime)
{
    internal void Validate()
    {
        OutboxEncodingV2.Text(Version);
        if (BatchLimit is < 1 or > 1000 || MaxAttempts is < 1 or > 100 || LeaseDuration <= TimeSpan.Zero ||
            LeaseDuration > TimeSpan.FromMinutes(15) || RetryDelay <= TimeSpan.Zero || MaximumRetryDelay < RetryDelay ||
            MaximumRetryDelay > TimeSpan.FromDays(1) || PublishBudget <= TimeSpan.Zero || PublishBudget >= LeaseDuration ||
            MaximumClaimLifetime < LeaseDuration || MaximumClaimLifetime > TimeSpan.FromDays(1))
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_POLICY_INVALID");
    }
}
/// <summary>Trusted host registration, never request-deserialized. All evidence is rechecked by the adapter under its own locks.</summary>
public sealed record OutboxOwnerFenceV2(string Schema, string Table, string KeyColumn, object Key);
public sealed record OutboxRegistrationV2(OutboxRootKeyV2 Root, string Schema, string WorkerId,
    string PublisherSetReference, string MigrationReference, string MappingReference, string WriterSetReference,
    string CutoverReference, IReadOnlyList<long> OwnerLockKeys, long RouteLockKey, OutboxPolicyV2 Policy,
    IReadOnlyList<OutboxOwnerFenceV2>? AuthorityRows = null);
public sealed record OutboxHandoverV2(string Reference, string PublisherSetReference, IReadOnlyList<Guid> NonterminalIds,
    bool UnknownTransactionsAuthoritativelyResolved);
public sealed record OutboxRetentionEvidenceV1(string PolicyId, string PolicyVersion, string TerminalOwnerReceipt,
    string RequiredConsumerSetVersion, bool AllConsumersComplete, bool ReferenceClosureComplete,
    bool HasHold, bool HasUnknownOrConflict, DateTimeOffset RetainUntilUtc, string DedupProtectionReference,
    string ArchiveReference, bool ArchiveVerified);
public sealed record OutboxRetentionCandidateV1(Guid NativeId, string ExpectedNativeVersion, string PolicyId, string PolicyVersion);

/// <summary>Trusted Owner adapter. Uses the supplied native connection/transaction; no network calls or commit.
/// ReadReceipt must use current read/field rights, independently of revoked new-write rights.</summary>
public interface IOutboxOwnerV2
{
    ValueTask<OutboxRegistrationV2> ResolveAsync(DbContext context, TrustedRequestContextV1 trusted, OutboxRootKeyV2 root, CancellationToken ct);
    ValueTask AuthorizeAsync(OutboxTransactionV2 transaction, OutboxAccessV2 access, CancellationToken ct);
    ValueTask<OutboxHandoverV2?> VerifyHandoverAsync(OutboxTransactionV2 transaction, Guid switchId, CancellationToken ct);
    ValueTask<OutboxRetentionEvidenceV1?> RetentionEligibilityAsync(OutboxTransactionV2 transaction, OutboxRetentionCandidateV1 candidate, CancellationToken ct);
}
public sealed class OutboxProtocolExceptionV2(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Serializable immutable tuple. Possession is not commit evidence or send permission.</summary>
public sealed record ClaimTicketV2(string ProfileId, int ProfileVersion, OutboxRootKeyV2 Root, Guid NativeId,
    string MessageId, string RawEnvelopeSha256, long AssignedEpoch, long ClaimEpoch, Guid ClaimId,
    string LeaseOwner, string LeaseToken, DateTimeOffset LeaseExpiresAtUtc, DateTimeOffset ClaimStartedAtUtc,
    string ExpectedNativeVersion, string ExpectedBindingVersion, string VersionSource, Guid VersionReceiptRef,
    Guid ClaimRequestId, Guid ClaimReceiptRef, string DispatchJson)
{
    public override string ToString() => "ClaimTicketV2 [redacted]";
}

public sealed class PreparedClaimV2
{
    internal PreparedClaimV2(Guid requestId, string input, IReadOnlyList<ClaimTicketV2> tickets)
        => (RequestId, Input, Tickets) = (requestId, input, Array.AsReadOnly(tickets.ToArray()));
    public Guid RequestId { get; }
    public string Input { get; }
    public IReadOnlyList<ClaimTicketV2> Tickets { get; }
    public OutboxDecisionV2 Decision => OutboxDecisionV2.PendingCommit;
}
public sealed class CommittedClaimTicketV2
{
    internal CommittedClaimTicketV2(ClaimTicketV2 ticket) => Ticket = ticket;
    public ClaimTicketV2 Ticket { get; }
    public Cp6OutboxDispatchMessage Message => JsonSerializer.Deserialize<Cp6OutboxDispatchMessage>(Ticket.DispatchJson)!;
}
public sealed record OutboxClaimResultV2(OutboxDecisionV2 Decision, PreparedClaimV2? Prepared = null,
    IReadOnlyList<CommittedClaimTicketV2>? Committed = null);
public sealed record OutboxMutationV2(Guid MutationId, ClaimTicketV2 Ticket, OutboxIntentV2 Intent,
    string PolicyVersion, string? SafeErrorCode = null, bool Retryable = false, string? SupportReference = null);
public sealed record OutboxRenewalV2(Guid MutationId, ClaimTicketV2 Ticket, string PolicyVersion, TimeSpan Duration);
public sealed record OutboxMutationResultV2(OutboxDecisionV2 Decision, Guid MutationId, string? Transition = null,
    Guid? ReceiptReference = null, ClaimTicketV2? RenewedTicket = null);
public sealed record OutboxSwitchResultV2(OutboxDecisionV2 Decision, long Epoch, string? ControlVersion = null);
public sealed record OutboxRetentionResultV1(OutboxDecisionV2 Decision, IReadOnlyList<Guid> Deleted, IReadOnlyList<Guid> Retained);

public static class OutboxEncodingV2
{
    public const string Profile = "cp6-outbox-lease-v2";
    public const string Mapping = "pg-xmin-v1";
    public static string Encode(uint token) => Mapping + ":" + token.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static byte[] NativeBytes(uint token) { var b = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, token); return b; }
    public static uint NativeValue(byte[] bytes) => bytes.Length == 4 ? BinaryPrimitives.ReadUInt32BigEndian(bytes) : throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TOKEN_INVALID");
    internal static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static void Text(string value) { if (string.IsNullOrWhiteSpace(value) || value.Length > 512 || value.Any(char.IsControl)) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_INPUT_INVALID"); }
    internal static void Key(Guid value) { if (value == Guid.Empty) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_INPUT_INVALID"); }
    internal static string Json<T>(T value) => JsonSerializer.Serialize(value);
}
