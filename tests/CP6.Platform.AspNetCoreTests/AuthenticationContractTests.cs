using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Platform.AspNetCoreTests;

public sealed class AuthenticationContractTests
{
    private const string Issuer = "https://identity.cp6.test";
    private const string Audience = "CP6.Web";

    [Fact]
    public async Task ValidRs256Token_WithRequiredClaims_IsAccepted()
    {
        var key = CreateKey("key-1");
        var manager = new RotatingConfigurationManager(Configuration(key));
        await using var provider = BuildProvider(manager);

        var result = await AuthenticateAsync(provider, CreateToken(key));

        Assert.True(result.Succeeded);
        Assert.Equal(Audience, result.Principal!.FindFirst("aud")!.Value);
        Assert.Equal(0, manager.RefreshCount);
    }

    [Fact]
    public async Task UnknownKid_RequestsJwksRefresh_AndAcceptsRotatedKey()
    {
        var oldKey = CreateKey("key-old");
        var newKey = CreateKey("key-new");
        var manager = new RotatingConfigurationManager(Configuration(oldKey), Configuration(newKey));
        await using var provider = BuildProvider(manager);

        var firstAttempt = await AuthenticateAsync(provider, CreateToken(newKey));
        var result = await AuthenticateAsync(provider, CreateToken(newKey));

        Assert.False(firstAttempt.Succeeded);
        Assert.True(result.Succeeded);
        Assert.Equal(1, manager.RefreshCount);
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("tenant_id")]
    [InlineData("jti")]
    [InlineData("iat")]
    [InlineData("nbf")]
    [InlineData("exp")]
    public async Task MissingRequiredClaim_FailsClosed(string missingClaim)
    {
        var key = CreateKey("key-required");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key, missingClaim: missingClaim));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task EmptyTenant_FailsClosed()
    {
        var key = CreateKey("key-tenant");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key, tenantId: Guid.Empty));

        Assert.False(result.Succeeded);
    }

    [Theory]
    [InlineData("https://wrong-issuer.cp6.test", Audience)]
    [InlineData(Issuer, "Wrong.Audience")]
    public async Task WrongIssuerOrAudience_FailsClosed(string issuer, string audience)
    {
        var key = CreateKey("key-boundary");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key, issuer: issuer, audience: audience));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ExpiredToken_FailsClosed()
    {
        var key = CreateKey("key-expired");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key, expiresAt: DateTime.UtcNow.AddMinutes(-1)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task FutureNotBefore_FailsClosed()
    {
        var key = CreateKey("key-future");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key, notBefore: DateTime.UtcNow.AddMinutes(5)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task MissingKid_FailsClosed()
    {
        var key = CreateKey(null);
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(key));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UnsignedToken_FailsClosed()
    {
        var key = CreateKey("key-signed");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));

        var result = await AuthenticateAsync(provider, CreateToken(signingKey: null));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Hs256Algorithm_IsRejected()
    {
        var key = CreateKey("key-rsa");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));
        var symmetric = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("not-an-rsa-key-but-long-enough-for-hs256"))
        {
            KeyId = key.KeyId
        };

        var result = await AuthenticateAsync(provider, CreateToken(symmetric, SecurityAlgorithms.HmacSha256));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ProblemWriter_ProducesSafeRfc9457Profile()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6ProblemDetails();
        await using var provider = services.BuildServiceProvider();
        var context = new DefaultHttpContext
        {
            RequestServices = provider,
            TraceIdentifier = "correlation-123"
        };
        context.Response.Body = new MemoryStream();

        await context.WriteCp6ProblemAsync(Cp6Problems.AuthenticationRequired);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        var root = document.RootElement;

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", root.GetProperty("code").GetString());
        Assert.Equal("cp6.error.authenticationRequired", root.GetProperty("messageKey").GetString());
        Assert.Equal("correlation-123", root.GetProperty("correlationId").GetString());
        Assert.Matches("^[0-9a-f]{32}$", root.GetProperty("traceId").GetString()!);
        Assert.False(root.TryGetProperty("detail", out _));
        Assert.DoesNotContain("exception", root.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthenticationChallenge_UsesTheSameSafeProblemProfile()
    {
        var key = CreateKey("key-challenge");
        await using var provider = BuildProvider(new RotatingConfigurationManager(Configuration(key)));
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            TraceIdentifier = "challenge-correlation"
        };
        context.Response.Body = new MemoryStream();

        await context.ChallengeAsync();
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("CP6_AUTHENTICATION_REQUIRED", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("challenge-correlation", document.RootElement.GetProperty("correlationId").GetString());
    }

    [Theory]
    [InlineData("good-correlation", "good-correlation")]
    [InlineData("contains space", null)]
    [InlineData("", null)]
    public async Task CorrelationMiddleware_PropagatesOnlySafeValues(string supplied, string? expected)
    {
        string? observed = null;
        var middleware = new Cp6CorrelationMiddleware(context =>
        {
            observed = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers[Cp6CorrelationMiddleware.HeaderName] = supplied;

        await middleware.InvokeAsync(context);

        Assert.NotNull(observed);
        if (expected is null)
        {
            Assert.Matches("^[0-9a-f]{32}$", observed!);
        }
        else
        {
            Assert.Equal(expected, observed);
        }
    }

    [Fact]
    public async Task MultipleCorrelationValues_AreReplaced()
    {
        string? observed = null;
        var middleware = new Cp6CorrelationMiddleware(context =>
        {
            observed = context.TraceIdentifier;
            return Task.CompletedTask;
        });
        var context = new DefaultHttpContext();
        context.Request.Headers.Append(Cp6CorrelationMiddleware.HeaderName, "first");
        context.Request.Headers.Append(Cp6CorrelationMiddleware.HeaderName, "second");

        await middleware.InvokeAsync(context);

        Assert.Matches("^[0-9a-f]{32}$", observed!);
    }

    [Fact]
    public void ProfileRejectsInsecureProductionMetadata()
    {
        var services = new ServiceCollection();
        var profile = new Cp6JwtBearerProfile
        {
            Authority = "http://identity.cp6.test",
            Issuer = Issuer,
            Audiences = [Audience],
            RequireHttpsMetadata = true,
            ClockSkew = TimeSpan.Zero
        };

        Assert.Throws<ArgumentException>(() => services.AddCp6JwtBearer(profile));
    }

    private static ServiceProvider BuildProvider(IConfigurationManager<OpenIdConnectConfiguration> manager)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCp6JwtBearer(Profile());
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.ConfigurationManager = manager;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private static Cp6JwtBearerProfile Profile() => new()
    {
        Authority = Issuer,
        Issuer = Issuer,
        Audiences = [Audience],
        ClockSkew = TimeSpan.Zero
    };

    private static async Task<AuthenticateResult> AuthenticateAsync(ServiceProvider provider, string token)
    {
        await using var scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        context.Request.Headers.Authorization = $"Bearer {token}";
        return await context.AuthenticateAsync();
    }

    private static RsaSecurityKey CreateKey(string? keyId)
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    private static OpenIdConnectConfiguration Configuration(SecurityKey key)
    {
        var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
        configuration.SigningKeys.Add(key);
        return configuration;
    }

    private static string CreateToken(
        SecurityKey? signingKey,
        string algorithm = SecurityAlgorithms.RsaSha256,
        string? missingClaim = null,
        Guid? tenantId = null,
        string issuer = Issuer,
        string audience = Audience,
        DateTime? expiresAt = null,
        DateTime? notBefore = null)
    {
        var now = DateTime.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = audience,
            ["sub"] = "user-123",
            ["tenant_id"] = (tenantId ?? Guid.NewGuid()).ToString(),
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["iat"] = Epoch(now),
            ["nbf"] = Epoch(notBefore ?? now.AddMinutes(-1)),
            ["exp"] = Epoch(expiresAt ?? now.AddMinutes(5))
        };
        if (missingClaim is not null)
        {
            claims.Remove(missingClaim);
        }

        var descriptor = new SecurityTokenDescriptor
        {
            Claims = claims,
            SigningCredentials = signingKey is null ? null : new SigningCredentials(signingKey, algorithm)
        };
        return new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false }.CreateToken(descriptor);
    }

    private static long Epoch(DateTime value) => new DateTimeOffset(value).ToUnixTimeSeconds();

    private sealed class RotatingConfigurationManager(
        OpenIdConnectConfiguration current,
        OpenIdConnectConfiguration? rotated = null) : IConfigurationManager<OpenIdConnectConfiguration>
    {
        private bool refreshRequested;

        public int RefreshCount { get; private set; }

        public Task<OpenIdConnectConfiguration> GetConfigurationAsync(CancellationToken cancel) =>
            Task.FromResult(refreshRequested && rotated is not null ? rotated : current);

        public void RequestRefresh()
        {
            refreshRequested = true;
            RefreshCount++;
        }
    }
}
