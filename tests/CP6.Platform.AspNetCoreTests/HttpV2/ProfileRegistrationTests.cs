using CP6.Platform.AspNetCore;
using CP6.Platform.AspNetCore.HttpV2;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class ProfileRegistrationTests
{
    [Fact]
    public void DefaultsPreserveBounds_AndRequireExplicitEnablement()
    {
        var p = new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead);
        Assert.Equal(Cp6HttpRetryMode.Disabled, p.RetryMode); Assert.Equal(2, p.RetryAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), p.AttemptTimeout); Assert.Equal(TimeSpan.FromSeconds(10), p.TotalTimeout);
        Assert.Equal(TimeSpan.FromSeconds(10), p.CircuitSamplingDuration); Assert.Equal(10, p.CircuitMinimumThroughput);
        Assert.Equal(TimeSpan.FromSeconds(30), p.CircuitBreakDuration);
        var write = new Cp6HttpV2Profile("code-exchange", Cp6HttpOperationKind.NonIdempotent, Cp6HttpRetryMode.OwnerProvenIdempotent, 5);
        Assert.Equal(0, write.RetryAttempts); Assert.Equal(Cp6HttpRetryMode.Disabled, write.RetryMode);
    }
    [Theory]
    [InlineData(-1, 100, 250)] [InlineData(6, 100, 250)] [InlineData(2, 99, 250)]
    [InlineData(2, 30001, 120000)] [InlineData(2, 100, 249)] [InlineData(2, 100, 120001)] [InlineData(2, 251, 250)]
    public void InvalidRangesFail(int retries, int attempt, int total) => Assert.ThrowsAny<ArgumentException>(() =>
        new Cp6HttpV2Profile("test", Cp6HttpOperationKind.IdempotentRead, retryAttempts: retries,
            attemptTimeout: TimeSpan.FromMilliseconds(attempt), totalTimeout: TimeSpan.FromMilliseconds(total)));
    [Fact]
    public void InvalidJitterAndUnknownEnumsFail()
    {
        Assert.ThrowsAny<ArgumentException>(() => new Cp6HttpV2Profile("x", (Cp6HttpOperationKind)99));
        Assert.ThrowsAny<ArgumentException>(() => new Cp6HttpV2Profile("x", Cp6HttpOperationKind.IdempotentRead, (Cp6HttpRetryMode)99));
        foreach (var jitter in new[] { double.NaN, double.PositiveInfinity, -0.1, 0.5001 })
            Assert.ThrowsAny<ArgumentException>(() => new Cp6HttpV2Profile("x", Cp6HttpOperationKind.IdempotentRead, jitterRatio: jitter));
    }
    [Fact]
    public void SameRegistrationIsIdempotent_DifferentProfileOrClientFails()
    {
        var services = new ServiceCollection(); var builder = services.AddHttpClient("read");
        var profile = new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead);
        builder.AddCp6HttpResilience(profile); var count = services.Count;
        builder.AddCp6HttpResilience(profile); Assert.Equal(count, services.Count);
        Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.NonIdempotent)));
        Assert.Throws<ArgumentException>(() => services.AddHttpClient("wrong").AddCp6HttpResilience(profile));
        using var provider = services.BuildServiceProvider();
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("read");
        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void LegacyAndV2CannotStackInEitherOrder(bool legacyFirst)
    {
        var services = new ServiceCollection(); var builder = services.AddHttpClient("read");
        var old = new Cp6HttpResilienceProfile("read", Cp6HttpOperationKind.IdempotentRead);
        var profile = new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead);
        if (legacyFirst)
        { builder.AddCp6HttpResilience(old); Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(profile)); }
        else
        {
            builder.AddCp6HttpResilience(profile); builder.AddCp6HttpResilience(old);
            using var provider = services.BuildServiceProvider();
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient("read"));
        }
    }
    [Fact]
    public void OtherRetryPipelineAndCompetingClientTimeoutFail()
    {
        var services = new ServiceCollection(); var builder = services.AddHttpClient("read");
        builder.AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead));
        builder.AddStandardResilienceHandler();
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient("read"));
        var other = new ServiceCollection(); var otherBuilder = other.AddHttpClient("read");
        otherBuilder.AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead));
        otherBuilder.ConfigureHttpClient(client => client.Timeout = TimeSpan.FromSeconds(10));
        using var otherProvider = other.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => otherProvider.GetRequiredService<IHttpClientFactory>().CreateClient("read"));
    }
    [Fact]
    public void EnabledWriteWithoutTrustedRegistrationFails()
    {
        var builder = new ServiceCollection().AddHttpClient("write");
        Assert.Throws<InvalidOperationException>(() => builder.AddCp6HttpResilience(new Cp6HttpV2Profile("write",
            Cp6HttpOperationKind.IdempotentWrite, Cp6HttpRetryMode.OwnerProvenIdempotent)));
    }
    [Theory]
    [InlineData(true, false)] [InlineData(false, true)]
    public void RedirectAndCookieOverridesCannotBypassFrozenTransport(bool redirect, bool cookies)
    {
        var services = new ServiceCollection(); var builder = services.AddHttpClient("read");
        builder.AddCp6HttpResilience(new Cp6HttpV2Profile("read", Cp6HttpOperationKind.IdempotentRead));
        builder.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = redirect, UseCookies = cookies });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IHttpClientFactory>().CreateClient("read"));
    }
}
