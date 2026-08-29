using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CP6.Platform.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CP6.Platform.AspNetCoreTests;

public sealed class GatewayContractTests
{
    [Fact]
    public async Task Proxy_StripsForgedIdentityMetadata_AndBuildsForwardedMetadata()
    {
        await using var backend = await StartBackendAsync();
        await using var gateway = await StartGatewayAsync(backend.Address);
        using var request = AuthorizedRequest("/crm/echo", "gateway-correlation");
        request.Headers.TryAddWithoutValidation("Forwarded", "for=203.0.113.10;proto=https");
        request.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.10");
        request.Headers.TryAddWithoutValidation("X-Forwarded-Client-Cert", "forged-certificate");
        request.Headers.TryAddWithoutValidation("X-User-Id", "attacker-user");
        request.Headers.TryAddWithoutValidation("X-Tenant", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("X-Tenant-Id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("X-Organization-Id", Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation("X-CP6-Identity-Subject", "attacker-subject");

        using var response = await gateway.Client.SendAsync(request);
        var observation = await response.Content.ReadFromJsonAsync<HeaderObservation>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(observation);
        Assert.True(observation.AuthorizationAccepted);
        Assert.Equal("gateway-correlation", observation.CorrelationId);
        Assert.False(observation.HasForwarded);
        Assert.False(observation.HasForwardedClientCertificate);
        Assert.False(observation.HasUserHeader);
        Assert.False(observation.HasExactTenantHeader);
        Assert.False(observation.HasTenantHeader);
        Assert.False(observation.HasOrganizationHeader);
        Assert.False(observation.HasCp6IdentityHeader);
        Assert.DoesNotContain("203.0.113.10", observation.ForwardedFor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnmatchedRoute_Returns404_WithoutCallingDestination()
    {
        await using var backend = await StartBackendAsync();
        await using var gateway = await StartGatewayAsync(backend.Address);

        using var response = await gateway.Client.GetAsync("/portal/not-a-crm-route");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, backend.RequestCount);
    }

    [Fact]
    public async Task DirectOrProxiedRequest_StillRequiresDestinationAuthentication()
    {
        await using var backend = await StartBackendAsync();
        await using var gateway = await StartGatewayAsync(backend.Address);
        using var direct = new HttpRequestMessage(HttpMethod.Get, "/crm/secure");
        direct.Headers.TryAddWithoutValidation("X-Tenant-Id", Guid.NewGuid().ToString());
        using var proxied = new HttpRequestMessage(HttpMethod.Get, "/crm/secure");
        proxied.Headers.TryAddWithoutValidation("X-Tenant-Id", Guid.NewGuid().ToString());

        using var directResponse = await backend.Client.SendAsync(direct);
        using var proxiedResponse = await gateway.Client.SendAsync(proxied);
        using var authorizedResponse = await gateway.Client.SendAsync(AuthorizedRequest("/crm/secure", "authorized-correlation"));

        Assert.Equal(HttpStatusCode.Unauthorized, directResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, proxiedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, authorizedResponse.StatusCode);
    }

    [Fact]
    public async Task RouteRateLimit_ReturnsSafeProblemDetailsBeforeDestinationCall()
    {
        await using var backend = await StartBackendAsync();
        await using var gateway = await StartGatewayAsync(backend.Address, permitLimit: 1);
        using var first = await gateway.Client.SendAsync(AuthorizedRequest("/crm/limited", "first-correlation"));
        using var second = await gateway.Client.SendAsync(AuthorizedRequest("/crm/limited", "limited-correlation"));
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        Assert.Equal("CP6_RATE_LIMIT_EXCEEDED", document.RootElement.GetProperty("code").GetString());
        Assert.Equal("limited-correlation", document.RootElement.GetProperty("correlationId").GetString());
        Assert.True(second.Headers.RetryAfter is not null || second.Headers.Contains("Retry-After"));
        Assert.Equal(1, backend.RequestCount);
    }

    [Fact]
    public void GatewayProfile_RejectsInsecureProductionDestination()
    {
        var profile = Profile(new Uri("http://crm.internal.example"), requireHttps: true);
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCp6Gateway(profile));
    }

    [Fact]
    public void GatewayProfile_RejectsUnknownCluster()
    {
        var profile = Profile(new Uri("https://crm.internal.example"));
        profile = new Cp6GatewayProfile
        {
            Clusters = profile.Clusters,
            Routes =
            [
                new Cp6GatewayRoute
                {
                    RouteId = "crm",
                    ClusterId = "missing-cluster",
                    MatchPath = "/crm/{**remainder}",
                    RateLimit = new Cp6GatewayRateLimit { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }
                }
            ]
        };
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(() => services.AddCp6Gateway(profile));
    }

    [Fact]
    public void GatewayProfile_RejectsMissingRateLimit()
    {
        var baseline = Profile(new Uri("https://crm.internal.example"));
        var profile = new Cp6GatewayProfile
        {
            Clusters = baseline.Clusters,
            Routes =
            [
                new Cp6GatewayRoute
                {
                    RouteId = "crm",
                    ClusterId = "crm-web",
                    MatchPath = "/crm/{**remainder}",
                    RateLimit = null!
                }
            ]
        };

        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddCp6Gateway(profile));
    }

    [Theory]
    [InlineData("crm/{**remainder}", 10)]
    [InlineData("/crm/{**remainder}?tenant=attacker", 10)]
    [InlineData("/crm/{**remainder}", 0)]
    [InlineData("/crm/{**remainder}", 10001)]
    public void GatewayProfile_RejectsUnsafePathOrLimit(string matchPath, int permitLimit)
    {
        var baseline = Profile(new Uri("https://crm.internal.example"));
        var profile = new Cp6GatewayProfile
        {
            Clusters = baseline.Clusters,
            Routes =
            [
                new Cp6GatewayRoute
                {
                    RouteId = "crm",
                    ClusterId = "crm-web",
                    MatchPath = matchPath,
                    RateLimit = new Cp6GatewayRateLimit
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1)
                    }
                }
            ]
        };

        Assert.ThrowsAny<ArgumentException>(() => new ServiceCollection().AddCp6Gateway(profile));
    }

    [Theory]
    [InlineData("X-User")]
    [InlineData("X-User-Id")]
    [InlineData("X-Tenant")]
    [InlineData("x-tenant-id")]
    [InlineData("X-Organization-Context")]
    [InlineData("X-CP6-Gateway-Validated")]
    [InlineData("Forwarded")]
    [InlineData("X-Forwarded-Client-Cert")]
    public void IdentityHeaderContract_IsCaseInsensitiveAndFailsClosed(string name)
    {
        Assert.True(Cp6GatewayHeaders.IsUntrustedIdentityHeader(name));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("Cookie")]
    [InlineData("X-Correlation-Id")]
    public void RequiredProtocolHeaders_AreNotClassifiedAsIdentityInjection(string name)
    {
        Assert.False(Cp6GatewayHeaders.IsUntrustedIdentityHeader(name));
    }

    private static HttpRequestMessage AuthorizedRequest(string path, string correlationId)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "p07-test-token");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", correlationId);
        return request;
    }

    private static Cp6GatewayProfile Profile(
        Uri destination,
        int permitLimit = 100,
        bool requireHttps = false) => new()
        {
            RequireHttpsDestinations = requireHttps,
            Routes =
        [
            new Cp6GatewayRoute
            {
                RouteId = "crm",
                ClusterId = "crm-web",
                MatchPath = "/crm/{**remainder}",
                RateLimit = new Cp6GatewayRateLimit
                {
                    PermitLimit = permitLimit,
                    Window = TimeSpan.FromMinutes(1)
                }
            }
        ],
            Clusters =
        [
            new Cp6GatewayCluster
            {
                ClusterId = "crm-web",
                Destinations =
                [
                    new Cp6GatewayDestination
                    {
                        DestinationId = "crm-web-1",
                        Address = destination
                    }
                ]
            }
        ]
        };

    private static async Task<RunningApplication> StartBackendAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var application = builder.Build();
        var requestCount = 0;
        application.Map("/{**path}", async context =>
        {
            Interlocked.Increment(ref requestCount);
            var accepted = string.Equals(
                context.Request.Headers.Authorization,
                "Bearer p07-test-token",
                StringComparison.Ordinal);
            if (!accepted)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            await context.Response.WriteAsJsonAsync(new HeaderObservation(
                accepted,
                context.Request.Headers["X-Correlation-Id"].ToString(),
                context.Request.Headers.ContainsKey("Forwarded"),
                context.Request.Headers.ContainsKey("X-Forwarded-Client-Cert"),
                context.Request.Headers.ContainsKey("X-User-Id"),
                context.Request.Headers.ContainsKey("X-Tenant"),
                context.Request.Headers.ContainsKey("X-Tenant-Id"),
                context.Request.Headers.ContainsKey("X-Organization-Id"),
                context.Request.Headers.ContainsKey("X-CP6-Identity-Subject"),
                context.Request.Headers["X-Forwarded-For"].ToString()));
        });
        await application.StartAsync();
        return new RunningApplication(application, () => Volatile.Read(ref requestCount));
    }

    private static async Task<RunningApplication> StartGatewayAsync(Uri destination, int permitLimit = 100)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddCp6Gateway(Profile(destination, permitLimit));
        var application = builder.Build();
        application.UseRouting();
        application.UseCp6Correlation();
        application.UseCp6GatewayRateLimiting();
        application.MapCp6Gateway();
        await application.StartAsync();
        return new RunningApplication(application, () => 0);
    }

    private sealed class RunningApplication(WebApplication application, Func<int> requestCount) : IAsyncDisposable
    {
        public Uri Address { get; } = GetAddress(application);

        public HttpClient Client { get; } = new() { BaseAddress = GetAddress(application) };

        public int RequestCount => requestCount();

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.StopAsync();
            await application.DisposeAsync();
        }

        private static Uri GetAddress(WebApplication application)
        {
            var server = application.Services.GetRequiredService<IServer>();
            var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses;
            var address = Assert.Single(addresses ?? []);
            return new Uri(address, UriKind.Absolute);
        }
    }

    private sealed record HeaderObservation(
        bool AuthorizationAccepted,
        string CorrelationId,
        bool HasForwarded,
        bool HasForwardedClientCertificate,
        bool HasUserHeader,
        bool HasExactTenantHeader,
        bool HasTenantHeader,
        bool HasOrganizationHeader,
        bool HasCp6IdentityHeader,
        string ForwardedFor);
}
