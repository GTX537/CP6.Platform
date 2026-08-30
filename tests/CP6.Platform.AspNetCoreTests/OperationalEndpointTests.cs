using System.Net;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using CP6.Platform.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CP6.Platform.AspNetCoreTests;

public sealed class OperationalEndpointTests
{
    private const string Secret = "connection-string-must-not-appear";

    [Fact]
    public async Task LiveEndpoint_DoesNotRunRegisteredChecks()
    {
        var invocationCount = 0;
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            new Cp6OperationalEndpointProfile([]),
            health => health.AddCheck(
                "external-live-check",
                () =>
                {
                    Interlocked.Increment(ref invocationCount);
                    return HealthCheckResult.Unhealthy(Secret);
                },
                tags: [Cp6HealthTags.Live]));

        using var response = await app.Client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, invocationCount);
        AssertSafeHealthEnvelope(document.RootElement, "Healthy", expectedComponents: 0);
        AssertSafeResponse(response, body);
    }

    [Theory]
    [InlineData("/health/startup", Cp6HealthTags.Startup, HealthStatus.Degraded)]
    [InlineData("/health/ready", Cp6HealthTags.Ready, HealthStatus.Unhealthy)]
    public async Task DependencyEndpoints_FilterTags_RedactDetails_AndFailOnAnyNonHealthy(
        string path,
        string selectedTag,
        HealthStatus selectedStatus)
    {
        var otherTagInvocationCount = 0;
        var otherTag = selectedTag == Cp6HealthTags.Startup ? Cp6HealthTags.Ready : Cp6HealthTags.Startup;
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            new Cp6OperationalEndpointProfile(["approved-component"]),
            health =>
            {
                health.AddCheck(
                    "approved-component",
                    () => new HealthCheckResult(
                        selectedStatus,
                        Secret,
                        new InvalidOperationException("exception-" + Secret),
                        new Dictionary<string, object> { ["database"] = "host-tenant-topic-" + Secret }),
                    tags: [selectedTag]);
                health.AddCheck(
                    "Unsafe_Component",
                    () => new HealthCheckResult(selectedStatus, "unsafe-name-" + Secret),
                    tags: [selectedTag]);
                health.AddCheck(
                    "private-database",
                    () => HealthCheckResult.Healthy("unlisted-host-" + Secret),
                    tags: [selectedTag]);
                health.AddCheck(
                    "other-tag",
                    () =>
                    {
                        Interlocked.Increment(ref otherTagInvocationCount);
                        return HealthCheckResult.Unhealthy(Secret);
                    },
                    tags: [otherTag]);
            });

        using var response = await app.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(0, otherTagInvocationCount);
        AssertSafeHealthEnvelope(document.RootElement, selectedStatus.ToString(), expectedComponents: 1);
        var component = Assert.Single(document.RootElement.GetProperty("components").EnumerateArray());
        Assert.Equal(
            new[] { "name", "status" },
            component.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal("approved-component", component.GetProperty("name").GetString());
        Assert.Equal(selectedStatus.ToString(), component.GetProperty("status").GetString());
        Assert.DoesNotContain("Unsafe_Component", body, StringComparison.Ordinal);
        Assert.DoesNotContain("private-database", body, StringComparison.Ordinal);
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task UnpublishedUnsafeCheck_AffectsAggregateButIsOmitted()
    {
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            new Cp6OperationalEndpointProfile([]),
            health => health.AddCheck(
                "Unsafe_Component",
                () => HealthCheckResult.Unhealthy(Secret),
                tags: [Cp6HealthTags.Ready]));

        using var response = await app.Client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        AssertSafeHealthEnvelope(document.RootElement, "Unhealthy", expectedComponents: 0);
        Assert.DoesNotContain("Unsafe_Component", body, StringComparison.Ordinal);
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task ReleaseEndpoint_ReturnsCompleteCandidateIdentity()
    {
        var observability = CandidateProfile();
        await using var app = await OperationalHost.StartAsync(
            observability,
            new Cp6OperationalEndpointProfile([]));

        using var response = await app.Client.GetAsync("/health/release");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        var release = root.GetProperty("release");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "observedAtUtc", "release", "schemaVersion", "status" }, PropertyNames(root));
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(
            new[]
            {
                "artifactDigest", "candidate", "contractBundleDigest", "gitSha", "service", "version"
            },
            PropertyNames(release));
        Assert.Equal(observability.ReleaseIdentity.Service, release.GetProperty("service").GetString());
        Assert.Equal(observability.ReleaseIdentity.Version, release.GetProperty("version").GetString());
        Assert.Equal(observability.ReleaseIdentity.GitSha, release.GetProperty("gitSha").GetString());
        Assert.True(release.GetProperty("candidate").GetBoolean());
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task ReleaseEndpoint_ReturnsExplicitNonCandidateIdentityWithoutEmptyDigests()
    {
        var observability = NonCandidateProfile();
        await using var app = await OperationalHost.StartAsync(
            observability,
            new Cp6OperationalEndpointProfile([]));

        using var response = await app.Client.GetAsync("/health/release");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var release = document.RootElement.GetProperty("release");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "candidate", "service", "version" }, PropertyNames(release));
        Assert.False(release.GetProperty("candidate").GetBoolean());
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task ReleaseEndpoint_RejectsInvalidIdentityWithoutEchoingIt()
    {
        var invalid = new Cp6ReleaseIdentity(
            "crm-api",
            "0.8.0-alpha.1",
            Secret,
            Secret,
            Secret,
            Cp6ReleaseMode.Candidate);
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            new Cp6OperationalEndpointProfile([]),
            overrideAccessor: new FixedReleaseIdentityAccessor(invalid));

        using var response = await app.Client.GetAsync("/health/release");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(new[] { "observedAtUtc", "schemaVersion", "status" }, PropertyNames(document.RootElement));
        Assert.Equal("Unhealthy", document.RootElement.GetProperty("status").GetString());
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task ReleaseEndpoint_RejectsValidButDriftedIdentity()
    {
        var drifted = CandidateProfile("portal-api").ReleaseIdentity;
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            new Cp6OperationalEndpointProfile([]),
            overrideAccessor: new FixedReleaseIdentityAccessor(drifted));

        using var response = await app.Client.GetAsync("/health/release");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.False(document.RootElement.TryGetProperty("release", out _));
        Assert.DoesNotContain("portal-api", body, StringComparison.Ordinal);
        AssertSafeResponse(response, body);
    }

    [Fact]
    public async Task MappingEqualProfileTwice_IsIdempotent()
    {
        var profile = new Cp6OperationalEndpointProfile(["approved-component"]);
        await using var app = await OperationalHost.StartAsync(
            CandidateProfile(),
            profile,
            mapAgain: new Cp6OperationalEndpointProfile(["approved-component"]));

        using var response = await app.Client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MappingDifferentProfile_FailsBeforeStartup()
    {
        var first = new Cp6OperationalEndpointProfile(["component-a"]);
        var drifted = new Cp6OperationalEndpointProfile(
            ["component-a"],
            readyPath: "/operations/ready");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => OperationalHost.StartAsync(CandidateProfile(), first, mapAgain: drifted));
    }

    [Theory]
    [InlineData("Unsafe_Component")]
    [InlineData("component/child")]
    [InlineData("component name")]
    public void Profile_RejectsUnsafePublishedComponentName(string componentName)
    {
        Assert.Throws<ArgumentException>(() => new Cp6OperationalEndpointProfile([componentName]));
    }

    private static void AssertSafeHealthEnvelope(JsonElement root, string expectedStatus, int expectedComponents)
    {
        Assert.Equal(
            new[] { "components", "observedAtUtc", "schemaVersion", "status" },
            PropertyNames(root));
        Assert.Equal("1.0.0", root.GetProperty("schemaVersion").GetString());
        Assert.Equal(expectedStatus, root.GetProperty("status").GetString());
        Assert.Equal(TimeSpan.Zero, root.GetProperty("observedAtUtc").GetDateTimeOffset().Offset);
        Assert.Equal(expectedComponents, root.GetProperty("components").GetArrayLength());
    }

    private static void AssertSafeResponse(HttpResponseMessage response, string body)
    {
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Secret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("exception-", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("duration", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("database", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("tenant", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("topic", body, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

    private static Cp6ObservabilityProfile CandidateProfile(string service = "crm-api")
    {
        var release = new Cp6ReleaseIdentity(
            service,
            "0.8.0-alpha.1",
            new string('a', 40),
            "sha256:" + new string('b', 64),
            "sha256:" + new string('c', 64),
            Cp6ReleaseMode.Candidate);
        return new Cp6ObservabilityProfile(service, release.Version, "test", "us-east", release);
    }

    private static Cp6ObservabilityProfile NonCandidateProfile()
    {
        var release = new Cp6ReleaseIdentity(
            "crm-api",
            "local",
            string.Empty,
            string.Empty,
            string.Empty,
            Cp6ReleaseMode.NonCandidate);
        return new Cp6ObservabilityProfile("crm-api", "local", "local", "local-dev", release);
    }

    private sealed class FixedReleaseIdentityAccessor(Cp6ReleaseIdentity current) : ICp6ReleaseIdentityAccessor
    {
        public Cp6ReleaseIdentity Current { get; } = current;
    }

    private sealed class OperationalHost(WebApplication application) : IAsyncDisposable
    {
        public HttpClient Client { get; } = new()
        {
            BaseAddress = GetAddress(application)
        };

        public static async Task<OperationalHost> StartAsync(
            Cp6ObservabilityProfile observability,
            Cp6OperationalEndpointProfile endpoints,
            Action<IHealthChecksBuilder>? configureHealth = null,
            ICp6ReleaseIdentityAccessor? overrideAccessor = null,
            Cp6OperationalEndpointProfile? mapAgain = null)
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddCp6Observability(observability);
            var health = builder.Services.AddHealthChecks();
            configureHealth?.Invoke(health);
            if (overrideAccessor is not null)
            {
                builder.Services.AddSingleton(overrideAccessor);
            }

            var application = builder.Build();
            application.MapCp6OperationalEndpoints(endpoints);
            if (mapAgain is not null)
            {
                application.MapCp6OperationalEndpoints(mapAgain);
            }

            await application.StartAsync();
            return new OperationalHost(application);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await application.StopAsync();
            await application.DisposeAsync();
        }

        private static Uri GetAddress(WebApplication application)
        {
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()?
                .Addresses;
            return new Uri(Assert.Single(addresses ?? []), UriKind.Absolute);
        }
    }
}
