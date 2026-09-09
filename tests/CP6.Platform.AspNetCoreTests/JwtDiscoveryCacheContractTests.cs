using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using CP6.Platform.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Platform.AspNetCoreTests;

public sealed class JwtDiscoveryCacheContractTests
{
    private const string Audience = "CP6.Web";

    [Fact]
    public void Registration_InstallsPlatformOwnedPlainConfigurationManager()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6JwtBearer(new Cp6JwtBearerProfile
        {
            Authority = "https://identity.cp6.test",
            Issuer = "https://identity.cp6.test",
            Audiences = [Audience]
        });
        using var provider = services.BuildServiceProvider();

        var manager = provider.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .ConfigurationManager;

        Assert.IsType<Cp6DeferredConfigurationManager>(manager);
        Assert.IsAssignableFrom<IConfigurationManager<OpenIdConnectConfiguration>>(manager);
        Assert.IsNotAssignableFrom<BaseConfigurationManager>(manager);
    }

    [Fact]
    public void Registration_DoesNotAllowFrameworkDefaultManagerToBeRestored()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6JwtBearer(new Cp6JwtBearerProfile
        {
            Authority = "https://identity.cp6.test",
            Issuer = "https://identity.cp6.test",
            Audiences = [Audience]
        });
        services.Configure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            options => options.ConfigurationManager = null);
        using var provider = services.BuildServiceProvider();

        var manager = provider.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .ConfigurationManager;

        Assert.IsType<Cp6DeferredConfigurationManager>(manager);
        Assert.IsNotAssignableFrom<BaseConfigurationManager>(manager);
    }

    [Fact]
    public async Task Registration_HonorsBackchannelConfiguredAfterAddCp6JwtBearer_WithoutTakingOwnership()
    {
        using var rsa = RSA.Create(2048);
        var profile = new Cp6JwtBearerProfile
        {
            Authority = "https://identity.cp6.test",
            Issuer = "https://identity.cp6.test",
            Audiences = [Audience]
        };
        var handler = new TrackingMetadataHandler(rsa, "key-backchannel", profile.Issuer);
        using var backchannel = new HttpClient(handler);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6JwtBearer(profile);
        services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            options => options.Backchannel = backchannel);
        var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .ConfigurationManager!;

        var configuration = await manager.GetConfigurationAsync(CancellationToken.None);

        Assert.Single(configuration.SigningKeys);
        Assert.Equal(2, handler.RequestCount);
        await provider.DisposeAsync();
        Assert.False(handler.Disposed);
    }

    [Fact]
    public async Task CredentialBearingConfiguredBackchannel_IsRejectedBeforeAnyFetch()
    {
        using var rsa = RSA.Create(2048);
        var profile = new Cp6JwtBearerProfile
        {
            Authority = "https://identity.cp6.test",
            Issuer = "https://identity.cp6.test",
            Audiences = [Audience]
        };
        var handler = new TrackingMetadataHandler(rsa, "key-credential", profile.Issuer);
        using var backchannel = new HttpClient(handler);
        backchannel.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "must-not-be-sent");
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6JwtBearer(profile);
        services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
            Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
            options => options.Backchannel = backchannel);
        await using var provider = services.BuildServiceProvider();
        var manager = provider.GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
            .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
            .ConfigurationManager!;

        await Assert.ThrowsAsync<Cp6JwtConfigurationProtocolException>(
            () => manager.GetConfigurationAsync(CancellationToken.None));
        Assert.Equal(0, handler.RequestCount);
    }

    [Fact]
    public async Task Registration_UsesFixedSameAuthorityJwksLocation()
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-fixed")
        {
            AdvertisedJwksUri = "http://127.0.0.1:1/attacker-jwks"
        };
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);
        Assert.Equal(1, state.JwksRequests);
        Assert.False(state.SawCredentialHeader);
    }

    [Fact]
    public async Task InitialFetchUnavailable_ReturnsGenericProblem401()
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-initial") { StatusCode = HttpStatusCode.ServiceUnavailable };
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", problem.GetProperty("code").GetString());
        Assert.DoesNotContain("ServiceUnavailable", problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, state.DiscoveryRequests);

        using var retry = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));
        Assert.Equal(HttpStatusCode.Unauthorized, retry.StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);
    }

    [Fact]
    public async Task MustRevalidate_RejectsKnownTokenWhenRefreshFailsAtSixtySeconds()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-must-revalidate")
        {
            CacheControl = "public, max-age=60, must-revalidate"
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(59));
        state.StatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(1));

        var response = await host.GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(1, state.JwksRequests);
    }

    [Fact]
    public async Task PermittedStalePolicy_AcceptsBelowHardAge_AndRejectsAtHardAge()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-hard-age") { CacheControl = "public, max-age=60" };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(899));
        state.StatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(1));

        var response = await host.GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    public async Task ExpectedUnavailableResponses_UseStalePolicyAndThirtySecondBackoff(HttpStatusCode statusCode)
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-backoff") { CacheControl = "public, max-age=60" };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.DiscoveryStatusCode = statusCode;

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(3, state.DiscoveryRequests);
    }

    [Fact]
    public async Task Http4xxIsProtocolFailureAndInvalidatesPermittedStaleKeys()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-4xx") { CacheControl = "public, max-age=60" };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.DiscoveryStatusCode = HttpStatusCode.NotFound;

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        state.DiscoveryStatusCode = HttpStatusCode.OK;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Fact]
    public async Task InvalidJwksAfterFreshness_InvalidatesPreviouslyTrustedKeys()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-invalidated") { CacheControl = "public, max-age=60" };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.JwksBodyOverride = "{\"keys\":[]}";

        var response = await host.GetProtectedAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Fact]
    public async Task ColdParallelBurst_PerformsOneSerializedFetch()
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-parallel");
        await using var host = await AuthenticationHost.StartAsync(state);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => host.GetProtectedAsync(token)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(1, state.DiscoveryRequests);
        Assert.Equal(1, state.JwksRequests);
    }

    [Fact]
    public async Task OrdinaryRefreshBurst_PerformsOneSerializedFetch()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-ordinary-refresh") { CacheControl = "public, max-age=60" };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(60));

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => host.GetProtectedAsync(token)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Fact]
    public async Task UnknownKid_FirstFailsThenOneControlledRefreshAcceptsRotatedKey()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var state = new IdentityState(oldRsa, "key-old");
        await using var host = await AuthenticationHost.StartAsync(state);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(
            CreateToken(oldRsa, "key-old", host.Issuer))).StatusCode);
        state.SetKey(newRsa, "key-new");
        var rotatedToken = CreateToken(newRsa, "key-new", host.Issuer);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(rotatedToken)).StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(rotatedToken)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Fact]
    public async Task ConcurrentUnknownKidBurst_PerformsOneControlledRefresh()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var state = new IdentityState(oldRsa, "key-burst-old");
        await using var host = await AuthenticationHost.StartAsync(state);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(
            CreateToken(oldRsa, "key-burst-old", host.Issuer))).StatusCode);
        state.SetKey(newRsa, "key-burst-new");
        var token = CreateToken(newRsa, "key-burst-new", host.Issuer);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);

        var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => host.GetProtectedAsync(token)));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.All(responses, response => Assert.Contains(
            response.StatusCode,
            new[] { HttpStatusCode.OK, HttpStatusCode.Unauthorized }));
        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Fact]
    public async Task FailedUnknownKidRefresh_KeepsFreshKnownKey_AndNeverAcceptsUnknownKey()
    {
        using var trustedRsa = RSA.Create(2048);
        using var unknownRsa = RSA.Create(2048);
        var state = new IdentityState(trustedRsa, "known-key");
        await using var host = await AuthenticationHost.StartAsync(state);
        var knownToken = CreateToken(trustedRsa, "known-key", host.Issuer);
        var unknownToken = CreateToken(unknownRsa, "unknown-key", host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(knownToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        state.StatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(knownToken)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Fact]
    public async Task UnknownKidRefresh_IsLimitedToOneAttemptPerSixtySeconds()
    {
        using var oldRsa = RSA.Create(2048);
        using var newRsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(oldRsa, "key-window-old");
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var oldToken = CreateToken(oldRsa, "key-window-old", host.Issuer);
        var newToken = CreateToken(newRsa, "key-window-new", host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(oldToken)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(newToken)).StatusCode);
        state.DiscoveryStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(newToken)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(59));
        state.DiscoveryStatusCode = HttpStatusCode.OK;
        state.SetKey(newRsa, "key-window-new");
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(newToken)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(newToken)).StatusCode);
        Assert.Equal(3, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("no-cache")]
    public async Task UnknownKidThrottle_DoesNotBlockRequiredNonReusableCacheRefresh(string directive)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-non-reusable") { CacheControl = directive };
        await using var host = await AuthenticationHost.StartAsync(state);
        var validToken = CreateToken(rsa, state.KeyId, host.Issuer);
        var unknownToken = CreateToken(rsa, "unknown-kid", host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(validToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);

        var response = await host.GetProtectedAsync(validToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(4, state.DiscoveryRequests);
        Assert.Equal(4, state.JwksRequests);
    }

    [Fact]
    public async Task UnknownKidThrottle_DoesNotBlockRequiredMustRevalidateRefresh()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-required-refresh")
        {
            CacheControl = "public, max-age=30, must-revalidate"
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var validToken = CreateToken(rsa, state.KeyId, host.Issuer);
        var unknownToken = CreateToken(rsa, "unknown-kid", host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(validToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        state.DiscoveryStatusCode = HttpStatusCode.ServiceUnavailable;
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(30));
        state.DiscoveryStatusCode = HttpStatusCode.OK;

        var response = await host.GetProtectedAsync(validToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Theory]
    [InlineData("no-store")]
    [InlineData("no-cache")]
    public async Task RevalidationDirectives_DoNotReuseKeysWithoutNetworkValidation(string directive)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-directive") { CacheControl = directive };
        await using var host = await AuthenticationHost.StartAsync(state);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);

        Assert.Equal(2, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Theory]
    [InlineData(null, 300)]
    [InlineData("public, max-age=3600", 300)]
    [InlineData("public, max-age=60", 60)]
    public async Task FreshnessFallbackAndCap_RefreshAtTheConfiguredBoundary(string? cacheControl, int boundarySeconds)
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-freshness") { CacheControl = cacheControl };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(boundarySeconds - 1));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Theory]
    [InlineData("cache-control")]
    [InlineData("age")]
    public async Task MalformedCacheHeaders_FailClosed(string header)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-header");
        if (header == "cache-control")
        {
            state.CacheControl = "public, max-age=not-a-number";
        }
        else
        {
            state.AgeHeaderOverride = "not-a-number";
        }
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ResponseAgeCountsTowardFreshnessAndHardAge()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-age")
        {
            CacheControl = "public, max-age=60",
            AgeSeconds = 59
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);

        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);

        state.AgeSeconds = 900;
        clock.Advance(TimeSpan.FromSeconds(60));
        var response = await host.GetProtectedAsync(token);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate-kid")]
    [InlineData("missing-kid")]
    [InlineData("private-rsa")]
    [InlineData("small-rsa")]
    [InlineData("invalid-base64")]
    [InlineData("empty-modulus")]
    [InlineData("exponent-zero")]
    [InlineData("exponent-one")]
    [InlineData("exponent-two")]
    [InlineData("exponent-even")]
    [InlineData("short-bitlength")]
    [InlineData("wrong-alg")]
    [InlineData("wrong-use")]
    [InlineData("too-many")]
    [InlineData("invalid-json")]
    [InlineData("oversize")]
    public async Task InvalidJwksProfiles_ReturnGenericProblem401(string profile)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-invalid")
        {
            JwksBodyOverride = IdentityState.CreateInvalidJwks(profile, rsa)
        };
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain("keys", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task JwksOptionalAlgorithmAndUse_AreAcceptedWhenAbsent()
    {
        using var rsa = RSA.Create(2048);
        var jwk = IdentityState.CreatePublicJwk(rsa, "key-optional");
        jwk.Remove("alg");
        jwk.Remove("use");
        var state = new IdentityState(rsa, "key-optional")
        {
            JwksBodyOverride = JsonSerializer.Serialize(new { keys = new[] { jwk } })
        };
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("exponent-zero")]
    [InlineData("exponent-one")]
    [InlineData("exponent-two")]
    [InlineData("exponent-even")]
    [InlineData("short-bitlength")]
    public async Task InvalidRsaValues_AreRejectedAsProtocolErrorsBeforeTokenValidation(string profile)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-rsa-protocol")
        {
            JwksBodyOverride = IdentityState.CreateInvalidJwks(profile, rsa)
        };
        await using var host = await AuthenticationHost.StartAsync(state);

        await Assert.ThrowsAsync<Cp6JwtConfigurationProtocolException>(
            () => host.ConfigurationManager.GetConfigurationAsync(CancellationToken.None));

        Assert.Equal(1, state.DiscoveryRequests);
        Assert.Equal(1, state.JwksRequests);
    }

    [Theory]
    [InlineData("wrong-issuer")]
    [InlineData("invalid-json")]
    [InlineData("oversize")]
    [InlineData("redirect")]
    public async Task InvalidDiscoveryProfiles_ReturnGenericProblem401(string profile)
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-metadata");
        switch (profile)
        {
            case "wrong-issuer":
                state.MetadataIssuerOverride = "http://wrong-issuer.invalid";
                break;
            case "invalid-json":
                state.DiscoveryBodyOverride = "not-json";
                break;
            case "oversize":
                state.DiscoveryBodyOverride = new string(' ', Cp6JwtConfigurationManager.MaximumBodyBytes + 1);
                break;
            case "redirect":
                state.DiscoveryRedirectLocation = "/redirect-target";
                break;
        }
        await using var host = await AuthenticationHost.StartAsync(state);

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);
        Assert.Equal(0, state.RedirectTargetRequests);
    }

    [Fact]
    public async Task CallerCancellation_PropagatesAndReleasesRefreshLock()
    {
        using var rsa = RSA.Create(2048);
        var state = new IdentityState(rsa, "key-cancel")
        {
            DelayTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task
        };
        await using var host = await AuthenticationHost.StartAsync(state);
        using var cancellation = new CancellationTokenSource();
        var request = host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer), cancellation.Token);
        await state.DiscoveryStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        state.DelayTask = Task.CompletedTask;

        var response = await host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
    }

    [Fact]
    public async Task FetchDeadline_CoversResponseBodyStreaming()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-deadline")
        {
            BodyDelayTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var request = host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));
        await state.DiscoveryBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        clock.Advance(Cp6JwtConfigurationManager.FetchTimeout);
        var response = await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(1, state.DiscoveryRequests);
        Assert.Equal(0, state.JwksRequests);
    }

    [Fact]
    public async Task UnavailableRefresh_UsesCompletionTimeForHardAgeDecision()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-completion-hard-age")
        {
            CacheControl = "public, max-age=60"
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(899));
        state.BodyDelayTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        var request = host.GetProtectedAsync(token);
        await state.DiscoveryBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Cp6JwtConfigurationManager.FetchTimeout);
        var response = await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MustRevalidateRefresh_UsesCompletionTimeForFreshnessDecision()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-completion-must-revalidate")
        {
            CacheControl = "public, max-age=60, must-revalidate"
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var knownToken = CreateToken(rsa, state.KeyId, host.Issuer);
        var unknownToken = CreateToken(rsa, "unknown-kid", host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(knownToken)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(59));
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.GetProtectedAsync(unknownToken)).StatusCode);
        state.BodyDelayTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        var request = host.GetProtectedAsync(knownToken);
        await state.DiscoveryBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Cp6JwtConfigurationManager.FetchTimeout);
        var response = await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FailureBackoff_StartsWhenTimedOutRefreshCompletes()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var state = new IdentityState(rsa, "key-completion-backoff")
        {
            CacheControl = "public, max-age=60"
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);
        var token = CreateToken(rsa, state.KeyId, host.Issuer);
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        clock.Advance(TimeSpan.FromSeconds(60));
        state.BodyDelayTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously).Task;

        var timedOutRequest = host.GetProtectedAsync(token);
        await state.DiscoveryBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(Cp6JwtConfigurationManager.FetchTimeout);
        Assert.Equal(HttpStatusCode.OK, (await timedOutRequest.WaitAsync(TimeSpan.FromSeconds(5))).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);

        state.BodyDelayTask = Task.CompletedTask;
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(2, state.DiscoveryRequests);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(HttpStatusCode.OK, (await host.GetProtectedAsync(token)).StatusCode);
        Assert.Equal(3, state.DiscoveryRequests);
        Assert.Equal(2, state.JwksRequests);
    }

    [Fact]
    public async Task SuccessfulNoStoreFetch_UsesBodyCompletionTimeForHardAgeDecision()
    {
        using var rsa = RSA.Create(2048);
        var clock = new AuthManualTimeProvider();
        var releaseBody = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var state = new IdentityState(rsa, "key-no-store-completion")
        {
            CacheControl = "no-store",
            AgeSeconds = 899,
            JwksBodyDelayTask = releaseBody.Task
        };
        await using var host = await AuthenticationHost.StartAsync(state, clock);

        var request = host.GetProtectedAsync(CreateToken(rsa, state.KeyId, host.Issuer));
        await state.JwksBodyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(1));
        releaseBody.SetResult();
        var response = await request.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", problem.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("https://user@identity.cp6.test")]
    [InlineData("https://identity.cp6.test?mode=test")]
    [InlineData("https://identity.cp6.test?")]
    [InlineData("https://identity.cp6.test#fragment")]
    [InlineData("https://identity.cp6.test#")]
    [InlineData("https://@identity.cp6.test")]
    [InlineData("ftp://identity.cp6.test")]
    public void ProfileRejectsUnsafeAuthorityUris(string authority)
    {
        var services = new ServiceCollection();
        var profile = new Cp6JwtBearerProfile
        {
            Authority = authority,
            Issuer = "https://identity.cp6.test",
            Audiences = [Audience]
        };

        Assert.Throws<ArgumentException>(() => services.AddCp6JwtBearer(profile));
    }

    [Theory]
    [InlineData("https://user@identity.cp6.test")]
    [InlineData("https://identity.cp6.test?mode=test")]
    [InlineData("https://identity.cp6.test?")]
    [InlineData("https://identity.cp6.test#fragment")]
    [InlineData("https://identity.cp6.test#")]
    [InlineData("https://@identity.cp6.test")]
    [InlineData("file:///identity")]
    public void ProfileRejectsUnsafeIssuerUris(string issuer)
    {
        var services = new ServiceCollection();
        var profile = new Cp6JwtBearerProfile
        {
            Authority = "https://identity.cp6.test",
            Issuer = issuer,
            Audiences = [Audience]
        };

        Assert.Throws<ArgumentException>(() => services.AddCp6JwtBearer(profile));
    }

    private static string CreateToken(RSA rsa, string kid, string issuer)
    {
        var now = DateTime.UtcNow;
        var key = new RsaSecurityKey(rsa) { KeyId = kid };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(
            new SecurityTokenDescriptor
            {
                TokenType = "at+jwt",
                Claims = new Dictionary<string, object>
                {
                    ["iss"] = issuer,
                    ["aud"] = Audience,
                    ["sub"] = "user-123",
                    ["tenant_id"] = Guid.NewGuid().ToString(),
                    ["jti"] = Guid.NewGuid().ToString("N"),
                    ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
                    ["nbf"] = new DateTimeOffset(now.AddMinutes(-1)).ToUnixTimeSeconds(),
                    ["exp"] = new DateTimeOffset(now.AddHours(1)).ToUnixTimeSeconds()
                },
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256)
            });
    }

    private sealed class AuthenticationHost(
        WebApplication identity,
        WebApplication resource,
        HttpClient client,
        string issuer,
        IConfigurationManager<OpenIdConnectConfiguration> configurationManager) : IAsyncDisposable
    {
        public string Issuer => issuer;
        public IConfigurationManager<OpenIdConnectConfiguration> ConfigurationManager => configurationManager;

        public async Task<HttpResponseMessage> GetProtectedAsync(string token, CancellationToken cancellationToken = default)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return await client.SendAsync(request, cancellationToken);
        }

        public static async Task<AuthenticationHost> StartAsync(
            IdentityState state,
            TimeProvider? timeProvider = null)
        {
            var identityBuilder = CreateBuilder();
            var identity = identityBuilder.Build();
            identity.MapGet("/.well-known/openid-configuration", async context =>
            {
                Interlocked.Increment(ref state.DiscoveryRequests);
                state.DiscoveryStarted.TrySetResult();
                state.SawCredentialHeader |= HasCredentials(context.Request);
                if (!state.DelayTask.IsCompleted)
                {
                    await state.DelayTask.WaitAsync(context.RequestAborted);
                }
                if (state.DiscoveryRedirectLocation is not null)
                {
                    context.Response.Redirect(state.DiscoveryRedirectLocation);
                    return;
                }
                context.Response.StatusCode = (int)state.DiscoveryStatusCode;
                if (state.DiscoveryStatusCode != HttpStatusCode.OK)
                {
                    await context.Response.WriteAsync("identity transport unavailable");
                    return;
                }

                context.Response.ContentType = "application/json";
                if (!state.BodyDelayTask.IsCompleted)
                {
                    await context.Response.WriteAsync("{\"issuer\":\"");
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    state.DiscoveryBodyStarted.TrySetResult();
                    await state.BodyDelayTask.WaitAsync(context.RequestAborted);
                }
                await context.Response.WriteAsync(state.DiscoveryBodyOverride ?? JsonSerializer.Serialize(new
                {
                    issuer = state.MetadataIssuerOverride ?? state.Issuer,
                    jwks_uri = state.AdvertisedJwksUri ?? new Uri(new Uri(state.Issuer), "/.well-known/jwks.json").AbsoluteUri
                }));
            });
            identity.MapGet("/redirect-target", () =>
            {
                Interlocked.Increment(ref state.RedirectTargetRequests);
                return Results.Json(new { issuer = state.Issuer });
            });
            identity.MapGet("/.well-known/jwks.json", async context =>
            {
                Interlocked.Increment(ref state.JwksRequests);
                state.SawCredentialHeader |= HasCredentials(context.Request);
                context.Response.StatusCode = (int)state.JwksStatusCode;
                if (state.JwksStatusCode != HttpStatusCode.OK)
                {
                    await context.Response.WriteAsync("jwks transport unavailable");
                    return;
                }

                if (state.CacheControl is not null)
                {
                    context.Response.Headers.CacheControl = state.CacheControl;
                }
                if (state.AgeHeaderOverride is not null)
                {
                    context.Response.Headers.Append("Age", state.AgeHeaderOverride);
                }
                else if (state.AgeSeconds is not null)
                {
                    context.Response.Headers.Age = state.AgeSeconds.Value.ToString();
                }
                context.Response.ContentType = "application/json";
                var body = state.JwksBodyOverride ?? state.CreateJwks();
                if (!state.JwksBodyDelayTask.IsCompleted)
                {
                    await context.Response.WriteAsync(body[..1]);
                    await context.Response.Body.FlushAsync(context.RequestAborted);
                    state.JwksBodyStarted.TrySetResult();
                    await state.JwksBodyDelayTask.WaitAsync(context.RequestAborted);
                    body = body[1..];
                }
                await context.Response.WriteAsync(body);
            });
            await identity.StartAsync();
            var identityAddress = Address(identity);
            state.Issuer = identityAddress;

            var resourceBuilder = CreateBuilder();
            if (timeProvider is not null)
            {
                resourceBuilder.Services.AddSingleton(timeProvider);
            }
            resourceBuilder.Services.AddAuthorization();
            resourceBuilder.Services.AddCp6JwtBearer(new Cp6JwtBearerProfile
            {
                Authority = identityAddress,
                Issuer = identityAddress,
                Audiences = [Audience],
                RequireHttpsMetadata = false,
                ClockSkew = TimeSpan.Zero
            });
            var resource = resourceBuilder.Build();
            resource.UseAuthentication();
            resource.UseAuthorization();
            resource.MapGet("/protected", () => Results.Ok()).RequireAuthorization();
            await resource.StartAsync();
            var client = new HttpClient { BaseAddress = new Uri(Address(resource)) };
            var configurationManager = Assert.IsAssignableFrom<IConfigurationManager<OpenIdConnectConfiguration>>(resource.Services
                .GetRequiredService<IOptionsMonitor<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>>()
                .Get(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme)
                .ConfigurationManager);
            return new AuthenticationHost(identity, resource, client, identityAddress, configurationManager);
        }

        public async ValueTask DisposeAsync()
        {
            client.Dispose();
            await resource.StopAsync();
            await resource.DisposeAsync();
            await identity.StopAsync();
            await identity.DisposeAsync();
        }

        private static WebApplicationBuilder CreateBuilder()
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = "Test",
                ApplicationName = typeof(AuthenticationHost).Assembly.FullName
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            return builder;
        }

        private static string Address(WebApplication application)
        {
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()?.Addresses;
            return Assert.Single(addresses ?? []);
        }

        private static bool HasCredentials(HttpRequest request) =>
            request.Headers.ContainsKey("Authorization") ||
            request.Headers.ContainsKey("Cookie") ||
            request.Headers.ContainsKey("Proxy-Authorization");
    }

    private sealed class IdentityState(RSA rsa, string keyId)
    {
        public RSA Rsa { get; private set; } = rsa;
        public string KeyId { get; private set; } = keyId;
        public string Issuer { get; set; } = "http://127.0.0.1";
        public string? AdvertisedJwksUri { get; init; }
        public HttpStatusCode StatusCode
        {
            get => DiscoveryStatusCode;
            set
            {
                DiscoveryStatusCode = value;
                JwksStatusCode = value;
            }
        }
        public HttpStatusCode DiscoveryStatusCode { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode JwksStatusCode { get; set; } = HttpStatusCode.OK;
        public string? CacheControl { get; set; } = "public, max-age=60, must-revalidate";
        public int? AgeSeconds { get; set; }
        public string? AgeHeaderOverride { get; set; }
        public string? JwksBodyOverride { get; set; }
        public string? DiscoveryBodyOverride { get; set; }
        public string? MetadataIssuerOverride { get; set; }
        public string? DiscoveryRedirectLocation { get; set; }
        public Task DelayTask { get; set; } = Task.CompletedTask;
        public Task BodyDelayTask { get; set; } = Task.CompletedTask;
        public Task JwksBodyDelayTask { get; set; } = Task.CompletedTask;
        public TaskCompletionSource DiscoveryStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DiscoveryBodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource JwksBodyStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int DiscoveryRequests;
        public int JwksRequests;
        public int RedirectTargetRequests;
        public bool SawCredentialHeader;

        public void SetKey(RSA nextRsa, string nextKeyId)
        {
            Rsa = nextRsa;
            KeyId = nextKeyId;
            JwksBodyOverride = null;
        }

        public string CreateJwks()
        {
            return JsonSerializer.Serialize(new { keys = new[] { CreateJwk(Rsa, KeyId) } });
        }

        public static Dictionary<string, object?> CreatePublicJwk(RSA rsa, string kid) => CreateJwk(rsa, kid);

        public static string CreateInvalidJwks(string profile, RSA rsa)
        {
            var valid = CreateJwk(rsa, "key-invalid");
            return profile switch
            {
                "empty" => "{\"keys\":[]}",
                "duplicate-kid" => JsonSerializer.Serialize(new { keys = new[] { valid, valid } }),
                "missing-kid" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "kid", null) } }),
                "private-rsa" => JsonSerializer.Serialize(new
                {
                    keys = new[] { With(valid, "d", Base64UrlEncoder.Encode(rsa.ExportParameters(true).D)) }
                }),
                "small-rsa" => CreateSmallRsaJwks(),
                "invalid-base64" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "n", "%%%") } }),
                "empty-modulus" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "n", string.Empty) } }),
                "exponent-zero" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "e", Base64UrlEncoder.Encode([0])) } }),
                "exponent-one" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "e", Base64UrlEncoder.Encode([1])) } }),
                "exponent-two" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "e", Base64UrlEncoder.Encode([2])) } }),
                "exponent-even" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "e", Base64UrlEncoder.Encode([4])) } }),
                "short-bitlength" => JsonSerializer.Serialize(new
                {
                    keys = new[] { With(valid, "n", Base64UrlEncoder.Encode(CreateShortBitLengthModulus())) }
                }),
                "wrong-alg" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "alg", "RS512") } }),
                "wrong-use" => JsonSerializer.Serialize(new { keys = new[] { With(valid, "use", "enc") } }),
                "too-many" => JsonSerializer.Serialize(new
                {
                    keys = Enumerable.Range(0, 33).Select(index => With(valid, "kid", $"key-{index}")).ToArray()
                }),
                "invalid-json" => "not-json",
                "oversize" => new string(' ', Cp6JwtConfigurationManager.MaximumBodyBytes + 1),
                _ => throw new ArgumentOutOfRangeException(nameof(profile))
            };
        }

        private static Dictionary<string, object?> CreateJwk(RSA rsa, string kid)
        {
            var parameters = rsa.ExportParameters(false);
            return new Dictionary<string, object?>
            {
                ["kty"] = "RSA",
                ["kid"] = kid,
                ["use"] = "sig",
                ["alg"] = "RS256",
                ["n"] = Base64UrlEncoder.Encode(parameters.Modulus),
                ["e"] = Base64UrlEncoder.Encode(parameters.Exponent)
            };
        }

        private static Dictionary<string, object?> With(
            Dictionary<string, object?> source,
            string name,
            object? value)
        {
            var copy = new Dictionary<string, object?>(source, StringComparer.Ordinal);
            if (value is null)
            {
                copy.Remove(name);
            }
            else
            {
                copy[name] = value;
            }
            return copy;
        }

        private static string CreateSmallRsaJwks()
        {
            using var small = RSA.Create(1024);
            return JsonSerializer.Serialize(new { keys = new[] { CreateJwk(small, "key-invalid") } });
        }

        private static byte[] CreateShortBitLengthModulus()
        {
            var modulus = new byte[256];
            modulus[0] = 0x7f;
            RandomNumberGenerator.Fill(modulus.AsSpan(1));
            modulus[^1] |= 1;
            return modulus;
        }
    }

    private sealed class AuthManualTimeProvider : TimeProvider
    {
        private readonly object gate = new();
        private readonly List<ManualTimer> timers = [];
        private DateTimeOffset utcNow = DateTimeOffset.UtcNow;
        private long timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override DateTimeOffset GetUtcNow()
        {
            lock (gate)
            {
                return utcNow;
            }
        }

        public override long GetTimestamp()
        {
            lock (gate)
            {
                return timestamp;
            }
        }

        public override ITimer CreateTimer(
            TimerCallback callback,
            object? state,
            TimeSpan dueTime,
            TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            return timer;
        }

        public void Advance(TimeSpan duration)
        {
            List<ManualTimer> due;
            lock (gate)
            {
                utcNow = utcNow.Add(duration);
                timestamp += duration.Ticks;
                due = timers.Where(timer => timer.IsDue(timestamp)).ToList();
                foreach (var timer in due)
                {
                    timer.MoveAfterFire(timestamp);
                }
            }

            foreach (var timer in due)
            {
                timer.Fire();
            }
        }

        private bool Schedule(ManualTimer timer, TimeSpan dueTime, TimeSpan period)
        {
            lock (gate)
            {
                if (timer.Disposed)
                {
                    return false;
                }

                if (!timers.Contains(timer))
                {
                    timers.Add(timer);
                }
                timer.DueAt = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : timestamp + dueTime.Ticks;
                timer.Period = period;
                return true;
            }
        }

        private void Remove(ManualTimer timer)
        {
            lock (gate)
            {
                timer.Disposed = true;
                timers.Remove(timer);
            }
        }

        private sealed class ManualTimer(
            AuthManualTimeProvider owner,
            TimerCallback callback,
            object? state) : ITimer
        {
            public long DueAt { get; set; } = long.MaxValue;
            public TimeSpan Period { get; set; } = Timeout.InfiniteTimeSpan;
            public bool Disposed { get; set; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => owner.Schedule(this, dueTime, period);

            public void Dispose() => owner.Remove(this);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }

            public bool IsDue(long now) => !Disposed && DueAt <= now;

            public void MoveAfterFire(long now)
            {
                DueAt = Period == Timeout.InfiniteTimeSpan ? long.MaxValue : now + Period.Ticks;
            }

            public void Fire() => callback(state);
        }
    }

    private sealed class TrackingMetadataHandler(RSA rsa, string kid, string issuer) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }
        public bool Disposed { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            var body = request.RequestUri?.AbsolutePath switch
            {
                "/.well-known/openid-configuration" => JsonSerializer.Serialize(new
                {
                    issuer,
                    jwks_uri = "https://attacker.invalid/jwks"
                }),
                "/.well-known/jwks.json" => JsonSerializer.Serialize(new
                {
                    keys = new[] { IdentityState.CreatePublicJwk(rsa, kid) }
                }),
                _ => throw new InvalidOperationException("Unexpected metadata request path.")
            };
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new StringContent(body)
            };
            response.Headers.CacheControl = CacheControlHeaderValue.Parse("public, max-age=60, must-revalidate");
            return Task.FromResult(response);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
