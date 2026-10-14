using System.Reflection;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;

namespace CP6.Platform.UnitTests.OutboxV2;

public sealed class OutboxV2ContractTests
{
    [Fact]
    public void Versioned_protocol_has_separate_prepared_and_committed_types()
    {
        var assembly = typeof(Cp6OutboxMessage).Assembly;
        foreach (var name in new[] { "PreparedClaimV2", "CommittedClaimTicketV2", "OutboxTransactionV2", "CallerTransactionOutboxV2" })
        {
            var type = assembly.GetType("CP6.Platform.EntityFramework.OutboxV2." + name);
            Assert.NotNull(type);
            if (name != "CallerTransactionOutboxV2") Assert.Empty(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
        }
    }

    [Fact]
    public void Legacy_claim_remains_without_epoch_or_fabricated_native_version()
    {
        var names = typeof(Cp6ClaimedOutboxMessage).GetProperties().Select(x => x.Name).ToArray();
        Assert.DoesNotContain("ClaimEpoch", names);
        Assert.DoesNotContain("NativeVersion", names);
        Assert.Equal(typeof(byte[]), typeof(Cp6OutboxMessage).GetProperty("RowVersion")!.PropertyType);
    }

    [Theory]
    [InlineData(-10, true)]
    [InlineData(0, false)]
    [InlineData(10, false)]
    public void Database_fence_is_strict_at_expiry_and_compares_instants(long ticks, bool live)
    {
        var expiry = new DateTimeOffset(2026, 10, 14, 0, 0, 0, TimeSpan.Zero);
        Assert.Equal(live, CallerTransactionOutboxV2.LeaseIsLive(expiry.AddTicks(ticks).ToOffset(TimeSpan.FromHours(8)), expiry));
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x01020304u)]
    [InlineData(uint.MaxValue)]
    public void Native_codec_preserves_unsigned_bits_and_network_order(uint value)
    {
        var encoded = OutboxEncodingV2.NativeBytes(value);
        Assert.Equal(value, OutboxEncodingV2.NativeValue(encoded));
        Assert.Equal((byte)(value >> 24), encoded[0]);
        Assert.Equal("pg-xmin-v1:" + value.ToString(System.Globalization.CultureInfo.InvariantCulture), OutboxEncodingV2.Encode(value));
        Assert.Throws<OutboxProtocolExceptionV2>(() => OutboxEncodingV2.NativeValue([1, 2, 3]));
    }

    [Fact]
    public void Root_identity_does_not_have_delimiter_ambiguity_and_ticket_diagnostics_are_redacted()
    {
        var tenant = Guid.NewGuid();
        var a = new OutboxRootKeyV2("a|b", tenant, "c", "r");
        var b = new OutboxRootKeyV2("a", tenant, "b|c", "r");
        Assert.NotEqual(a.Id, b.Id);
        var ticket = new ClaimTicketV2("profile", 1, a, Guid.NewGuid(), "sensitive-message", "raw-hash", 1, 1, Guid.NewGuid(),
            "worker", "secret-token", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "native-version", "binding-version", "ClaimCommit",
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "private-payload");
        Assert.Equal("ClaimTicketV2 [redacted]", ticket.ToString());
    }
}
