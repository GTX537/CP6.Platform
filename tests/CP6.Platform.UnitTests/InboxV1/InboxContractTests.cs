using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.InboxV1;
using System.Text;
using System.Security.Cryptography;

namespace CP6.Platform.UnitTests.InboxV1;

public sealed class InboxContractTests
{
    [Fact]
    public void Caller_owned_entry_exists_separately_from_legacy_processor()
    {
        var assembly = typeof(Cp6InboxDelivery).Assembly;
        Assert.NotNull(assembly.GetType("CP6.Platform.EntityFramework.InboxV1.CallerTransactionInboxV1"));
    }

    internal static Cp6InboxDelivery Delivery(string raw = "{\"data\":{\"n\":1.00}}") =>
        new("consumer", "message", Guid.Parse("22222222-2222-2222-2222-222222222222"), "topic", "partition", Encoding.UTF8.GetBytes(raw), "aggregate", 2);
    internal static InboxContractBindingV1 Binding => new("owner", "operation", "v1", "owner-input", "codec-v1",
        new string('a', 64), "type", "source", "region", "schema", "bundle", "registration");
    private static InboxAuthorityVersionV1 Version(InboxInputV1 i, int version, string? hash = null) =>
        new(i.Delivery.TenantId, i.Delivery.AggregateId, version, i.Binding.EventType, hash ?? i.DataPayloadSha256, "owner-original-lineage");

    [Fact]
    public void Decisions_are_closed_and_pending_cannot_mint_committed_transport_success()
    {
        Assert.Equal(7, Enum.GetValues<PreCheckpointIdentityDecisionV1>().Length);
        Assert.Empty(typeof(CommittedDeliveryOutcome).GetConstructors());
        Assert.Empty(typeof(PendingCommitResult).GetConstructors());
        Assert.DoesNotContain(typeof(PendingCommitResult).GetProperties(), p => p.Name is "TransportOutcome" or "VisibleResult");
    }

    [Theory]
    [InlineData("{\"data\":{\"n\":1.00}}", "{\"n\":1.00}")]
    [InlineData(" { \"meta\": 1, \"data\" : [ 1,2.00 ] } ", "[ 1,2.00 ]")]
    [InlineData("{\"data\":\"\\u4f60\"}", "\"\\u4f60\"")]
    [InlineData("{\"data\":null}", "null")]
    public void Bytes_and_digest_domains_remain_exact(string raw, string data)
    {
        var callerBuffer = Encoding.UTF8.GetBytes(raw);
        var d = Delivery(raw) with { Payload = callerBuffer };
        var i = new InboxInputV1(d, Binding);
        Array.Fill(callerBuffer, (byte)0);
        Assert.Equal(Encoding.UTF8.GetBytes(raw), i.OriginalEnvelope.ToArray());
        Assert.Equal(Encoding.UTF8.GetBytes(data), i.OriginalDataPayload.ToArray());
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data))).ToLowerInvariant(), i.DataPayloadSha256);
        Assert.NotEqual(i.DataPayloadSha256, i.RawEnvelopeSha256);
        Assert.NotEqual(i.RawEnvelopeSha256, i.Binding.InputDigest);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"data\":1,\"data\":2}")]
    [InlineData("{\"data\":{\"a\":1,\"\\u0061\":2}}")]
    [InlineData("{\"data\":[{\"a\":1,\"a\":2}]}")]
    [InlineData("{\"data\":1}{}")]
    [InlineData("{\"data\":1,}")]
    public void Malformed_or_ambiguous_original_json_is_rejected(string raw) =>
        Assert.Throws<InboxProtocolExceptionV1>(() => new InboxInputV1(Delivery(raw), Binding));

    [Fact]
    public void Seven_decisions_use_original_transport_then_authority_and_history()
    {
        var i = new InboxInputV1(Delivery(), Binding);
        var original = InboxTransportV1.From(i);
        Assert.Equal(PreCheckpointIdentityDecisionV1.FreshCandidate, InboxContentDecisionV1.Evaluate(i, null, new(true, null, [])));
        Assert.Equal(PreCheckpointIdentityDecisionV1.DependencyUnavailable, InboxContentDecisionV1.Evaluate(i, original, new(false, null, [])));
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportDuplicate, InboxContentDecisionV1.Evaluate(i, original, new(true, Version(i, 4), [])));
        Assert.Equal(PreCheckpointIdentityDecisionV1.OlderVersionIgnored, InboxContentDecisionV1.Evaluate(i, null, new(true, Version(i, 4), [])));
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionEqual, InboxContentDecisionV1.Evaluate(i, null, new(true, Version(i, 2), [])));
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionConflict, InboxContentDecisionV1.Evaluate(i, original, new(true, Version(i, 4), [Version(i, 2, new string('b', 64))])));
        var changed = new InboxInputV1(Delivery("{ \"data\":{\"n\":1.00}}"), Binding);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict, InboxContentDecisionV1.Evaluate(changed, original, new(false, null, [])));
    }

    [Fact]
    public void Authority_wrong_tenant_or_missing_lineage_never_becomes_available_from_incoming()
    {
        var i = new InboxInputV1(Delivery(), Binding);
        foreach (var a in new[] { Version(i, 2) with { TenantId = Guid.NewGuid() }, Version(i, 2) with { SourceReference = "" } })
            Assert.Equal(PreCheckpointIdentityDecisionV1.DependencyUnavailable, InboxContentDecisionV1.Evaluate(i, null, new(true, a, [])));
    }

    [Fact]
    public void Same_data_with_different_legitimate_transport_is_equal_and_binding_changes_conflict()
    {
        var i = new InboxInputV1(Delivery(), Binding);
        var changed = new InboxInputV1(Delivery("{ \"data\":{\"n\":1.00}}") with { MessageId = "new-message" }, Binding);
        Assert.Equal(i.DataPayloadSha256, changed.DataPayloadSha256);
        Assert.NotEqual(i.RawEnvelopeSha256, changed.RawEnvelopeSha256);
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionEqual, InboxContentDecisionV1.Evaluate(changed, null, new(true, Version(i, 2), [])));
        var wrongTenant = new InboxInputV1(Delivery() with { TenantId = Guid.NewGuid() }, Binding);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict, InboxContentDecisionV1.Evaluate(wrongTenant, InboxTransportV1.From(i), new(true, null, [])));
    }

    [Fact]
    public void Same_transport_cannot_be_rebound_to_another_original_operation()
    {
        var i = new InboxInputV1(Delivery(), Binding);
        var changed = new InboxInputV1(Delivery(), Binding with { OperationKey = "replacement-operation" });
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict,
            InboxContentDecisionV1.Evaluate(changed, InboxTransportV1.From(i), new(true, Version(i, 2), [])));
    }

    [Fact]
    public void Original_key_only_recovery_has_no_transport_success_capability()
    {
        var type = typeof(CallerTransactionInboxV1).Assembly.GetType("CP6.Platform.EntityFramework.InboxV1.CommittedOriginalResultV1");
        Assert.NotNull(type);
        Assert.Empty(type.GetConstructors());
        Assert.DoesNotContain(type.GetProperties(), p => p.Name is "TransportOutcome" or "Decision");
        Assert.NotNull(typeof(CallerTransactionInboxV1).GetMethod("ReadOriginalOperationAsync"));
    }
}
