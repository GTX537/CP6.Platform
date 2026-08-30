using CP6.Platform.Abstractions;
using CP6.Platform.EntityFramework;

namespace CP6.Platform.UnitTests;

public sealed class TransactionalMessagingContractTests
{
    [Fact]
    public void Defaults_FreezeApprovedLeaseRetryAndRetentionPolicy()
    {
        var options = new Cp6TransactionalMessagingOptions();

        options.Validate();
        Assert.Equal(TimeSpan.FromSeconds(30), options.OutboxLeaseDuration);
        Assert.Equal(100, options.DispatchBatchSize);
        Assert.Equal(10, options.MaxOutboxAttempts);
        Assert.Equal(10, options.MaxInboxAttempts);
        Assert.Equal(TimeSpan.FromDays(7), options.PublishedOutboxRetention);
        Assert.Equal(TimeSpan.FromDays(30), options.ProcessedInboxRetention);
        Assert.Equal(TimeSpan.FromDays(90), options.DeadLetterRetention);
    }

    [Fact]
    public void Options_RejectUnboundedOrNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6TransactionalMessagingOptions { OutboxLeaseDuration = TimeSpan.Zero }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6TransactionalMessagingOptions { DispatchBatchSize = 1001 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6TransactionalMessagingOptions { MaxOutboxAttempts = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6TransactionalMessagingOptions { MaxInboxAttempts = 101 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6TransactionalMessagingOptions { DeadLetterRetention = TimeSpan.Zero }.Validate());
    }

    [Fact]
    public void Exceptions_ExposeOnlyContentSafeFailureMetadata()
    {
        var publish = new Cp6OutboxPublishException("CP6_BROKER_UNAVAILABLE", true, "support-17");
        var process = new Cp6InboxProcessingException("CP6_SQL_TRANSIENT", true, "support-18");

        Assert.Equal("CP6_BROKER_UNAVAILABLE", publish.ErrorCode);
        Assert.Equal("support-17", publish.SupportReference);
        Assert.Equal("CP6_SQL_TRANSIENT", process.ErrorCode);
        Assert.Equal("support-18", process.SupportReference);
        Assert.Throws<ArgumentException>(() => new Cp6OutboxPublishException("unsafe payload value", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Cp6InboxProcessingException("CP6_VALID", true, new string('x', 129)));
    }

    [Fact]
    public void TelemetryContract_FreezesEntityFrameworkSourceMeterAndOperations()
    {
        Assert.Equal("CP6.Platform.EntityFramework", Cp6TelemetrySources.EntityFramework);
        Assert.Equal("CP6.Platform.EntityFramework", Cp6TelemetryMeters.EntityFramework);
        Assert.Equal("cp6.outbox.dispatch", Cp6TelemetryConventions.OutboxDispatchOperation);
        Assert.Equal("cp6.inbox.process", Cp6TelemetryConventions.InboxProcessOperation);
    }
}
