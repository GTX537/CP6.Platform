using CP6.Platform.Abstractions;

namespace CP6.Platform.UnitTests;

public sealed class TelemetryConventionTests
{
    [Fact]
    public void SourcesAndMeters_UseTheExactApprovedNames()
    {
        var expected = new[]
        {
            "CP6.Platform.AspNetCore",
            "CP6.Platform.Messaging",
            "CP6.Platform.EntityFramework"
        };

        Assert.Equal(expected, Cp6TelemetrySources.All);
        Assert.Equal(expected, Cp6TelemetryMeters.All);
    }

    [Fact]
    public void Operations_UseTheExactApprovedNames()
    {
        Assert.Equal(
            new[]
            {
                "cp6.http.outbound",
                "cp6.messaging.dapr.invoke",
                "cp6.messaging.publish",
                "cp6.messaging.consume",
                "cp6.outbox.dispatch",
                "cp6.inbox.process"
            },
            Cp6TelemetryConventions.AllOperations);
    }

    [Fact]
    public void MetricTags_AreAnExactLowCardinalityAllowlist()
    {
        Assert.Equal(
            new[]
            {
                "cp6.error.code",
                "cp6.http.operation_kind",
                "cp6.messaging.disposition",
                "cp6.messaging.transport",
                "cp6.operation",
                "cp6.outcome",
                "cp6.region"
            },
            Cp6TelemetryConventions.AllowedMetricTags.Order(StringComparer.Ordinal));

        foreach (var tag in Cp6TelemetryConventions.AllowedMetricTags)
        {
            Cp6TelemetryConventions.EnsureAllowedMetricTag(tag);
        }
    }

    [Theory]
    [InlineData("tenant.id")]
    [InlineData("user.id")]
    [InlineData("resource.id")]
    [InlineData("correlation.id")]
    [InlineData("event.id")]
    [InlineData("trace.id")]
    [InlineData("url.full")]
    [InlineData("server.address")]
    [InlineData("exception.message")]
    public void MetricTags_RejectHighCardinalityOrSensitiveNames(string tag)
    {
        var exception = Assert.Throws<ArgumentException>(
            () => Cp6TelemetryConventions.EnsureAllowedMetricTag(tag));

        Assert.DoesNotContain(tag, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthTags_UseTheExactApprovedNames()
    {
        Assert.Equal("live", Cp6HealthTags.Live);
        Assert.Equal("startup", Cp6HealthTags.Startup);
        Assert.Equal("ready", Cp6HealthTags.Ready);
        Assert.Equal(new[] { "live", "startup", "ready" }, Cp6HealthTags.All);
    }
}
