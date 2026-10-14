using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class HealthIdentityAndPermissionTests
{
    [Fact]
    public async Task IdentityFreshnessGatesRelevantEffectsWhileAuthorizedReadRemainsAvailable()
    {
        var clock = new ObservationClock(DateTimeOffset.UtcNow); var binding = RuntimeIdentityTests.Binding(start: clock.Now.AddSeconds(-1));
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes], clock);
        var identity = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [RuntimeIdentityTests.Pin(bytes)]), observer, clock);
        var builder = Builder(); var probes = 0;
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAuthenticatedUser()));
        builder.Services.AddHealthChecks().AddCheck("dependency", () => { Interlocked.Increment(ref probes); return HealthCheckResult.Healthy(); }, tags: ["ready-v1"]);
        var profile = new HealthProfileV1("operator", [new HealthCheckBindingV1("dependency", "dependency", HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.Local, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(20), ProbePolicyV1.ReadOnly, true)],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["dependency"]), new CapabilityRouteV1("/write", CapabilityV1.NewEffect, "CP6.Platform.Contracts", ["dependency"])]);
        builder.Services.AddCp6HealthV1(profile, identity);
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/ops/v1/ready")).StatusCode); Assert.Equal(0, probes);
        client.DefaultRequestHeaders.Add("fixture-operator", "yes");
        var initial = await client.GetAsync("/ops/v1/ready"); Assert.Equal(HttpStatusCode.OK, initial.StatusCode);
        Assert.Contains("\"newEffectsAvailable\":true", await initial.Content.ReadAsStringAsync());
        clock.Now += TimeSpan.FromMinutes(2);
        var stale = await client.GetAsync("/ops/v1/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, stale.StatusCode);
        var body = await stale.Content.ReadAsStringAsync(); Assert.Contains("\"readAvailable\":true", body); Assert.Contains("\"newEffectsAvailable\":false", body);
    }

    [Fact]
    public void EffectCapabilityCannotBeDeclaredWithoutARelevantArtifactBinding()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/write", CapabilityV1.NewEffect, requiredChecks: ["dependency"]));
    }

    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); return builder;
    }
    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers["fixture-operator"] == "yes"
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "operator")], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
}
