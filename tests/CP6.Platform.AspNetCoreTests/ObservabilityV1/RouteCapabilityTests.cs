using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class RouteCapabilityTests
{
    [Fact]
    public async Task WriteOnlyDependencyFailurePreservesReadRouteAndHiddenFailureKeepsOverall503()
    {
        var builder = Builder();
        builder.Services.AddHealthChecks().AddCheck("read-store", () => HealthCheckResult.Healthy(), tags: ["ready-v1"])
            .AddCheck("write-store", () => HealthCheckResult.Unhealthy(), tags: ["ready-v1"]);
        var profile = new HealthProfileV1("operator", [Check("read-store"), Check("write-store", false)],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["read-store"]),
             new CapabilityRouteV1("/write", CapabilityV1.NewEffect, "CP6.Platform.Contracts", ["write-store"])]);
        builder.Services.AddCp6HealthV1(profile, new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = Client(host); using var response = await client.GetAsync("/ops/v1/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<HealthObservationV1>(); Assert.NotNull(report);
        Assert.True(report.ReadAvailable); Assert.False(report.NewEffectsAvailable); Assert.False(report.Healthy);
        Assert.True(report.Routes.Single(r => r.Template == "/read").Available);
        Assert.False(report.Routes.Single(r => r.Template == "/write").Available);
        Assert.DoesNotContain(report.Components, c => c.Name == "write-store");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ModuleBMismatchOnlyBlocksBUnlessCommonHostBindingAlsoMismatches(bool wrongHost)
    {
        var binding = RuntimeIdentityTests.Binding();
        var assemblyA = typeof(Cp6ReleaseIdentity).Assembly; var assemblyB = typeof(OwnedModuleObserverV1).Assembly;
        var bytesA = File.ReadAllBytes(assemblyA.Location); var bytesB = File.ReadAllBytes(assemblyB.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytesA, bytesB]);
        var pinA = Pin(assemblyA, bytesA); var pinB = Pin(assemblyB, bytesB);
        var expected = new RuntimeManifestV1(wrongHost ? RuntimeIdentityTests.Binding(service: "other") : binding,
            ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [pinA, new ModulePinV1(pinB.AssemblyName, pinB.AssemblyVersion, "sha256:" + new string('0', 64))]);
        var builder = Builder(); builder.Services.AddHealthChecks().AddCheck("store", () => HealthCheckResult.Healthy(), tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(new HealthProfileV1("operator", [Check("store")],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["store"]),
             new CapabilityRouteV1("/a", CapabilityV1.NewEffect, pinA.AssemblyName, ["store"]),
             new CapabilityRouteV1("/b", CapabilityV1.NewEffect, pinB.AssemblyName, ["store"])]), new RuntimeIdentityEvaluatorV1(expected, observer));
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = Client(host); using var response = await client.GetAsync("/ops/v1/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var report = await response.Content.ReadFromJsonAsync<HealthObservationV1>(); Assert.NotNull(report);
        Assert.True(report.ReadAvailable); Assert.False(report.Healthy);
        Assert.Equal(!wrongHost, report.Routes.Single(r => r.Template == "/a").Available);
        Assert.Equal(wrongHost ? IdentityVerdictV1.Mismatch : IdentityVerdictV1.Match, report.Routes.Single(r => r.Template == "/a").RuntimeIdentity);
        Assert.False(report.Routes.Single(r => r.Template == "/b").Available);
        Assert.Equal(IdentityVerdictV1.Mismatch, report.RuntimeIdentity);
    }

    [Fact]
    public void MissingEmptyDuplicateUnknownAndWrongPurposeRouteCheckInventoriesFailClosed()
    {
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/read", CapabilityV1.Read));
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: []));
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["store", "store"]));
        Assert.Throws<ArgumentException>(() => new HealthProfileV1("operator", [Check("store")],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["missing"])]));
        var startup = new HealthCheckBindingV1("start", "start", HealthPurposeV1.Startup, "startup-v1", DependencyKindV1.Local, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), ProbePolicyV1.ReadOnly, true);
        Assert.Throws<ArgumentException>(() => new HealthProfileV1("operator", [startup],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["start"])]));
        var source = new[] { "store" }; var route = new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: source); source[0] = "other";
        Assert.Equal("store", Assert.Single(route.RequiredChecks));
    }

    private static HealthCheckBindingV1 Check(string name, bool publish = true) => new(name, name, HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.Local, TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(10), ProbePolicyV1.ReadOnly, publish);
    private static ModulePinV1 Pin(Assembly assembly, byte[] bytes) => new(assembly.GetName().Name!, assembly.GetName().Version!.ToString(), "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true))); return builder;
    }
    private static HttpClient Client(WebApplication host) => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
}
