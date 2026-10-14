using System.Net;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class OperationalNoStoreRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("live", false, HttpStatusCode.OK)]
    [InlineData("identity", false, HttpStatusCode.OK)]
    [InlineData("startup", false, HttpStatusCode.OK)]
    [InlineData("ready", false, HttpStatusCode.OK)]
    [InlineData("startup", true, HttpStatusCode.ServiceUnavailable)]
    [InlineData("ready", true, HttpStatusCode.ServiceUnavailable)]
    public async Task EveryOperationalResponseDisablesStorage(string route, bool failedDependency, HttpStatusCode expected)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks()
            .AddCheck("start", () => failedDependency ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy(), tags: ["startup-v1"])
            .AddCheck("store", () => failedDependency ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy(), tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(new HealthProfileV1("operator",
            [Check("start", HealthPurposeV1.Startup, "startup-v1"), Check("store", HealthPurposeV1.Ready, "ready-v1")],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["store"])]), new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var response = await client.GetAsync("/ops/v1/" + route);
        Assert.Equal(expected, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore == true, $"{route} must send Cache-Control:no-store; actual={response.Headers.CacheControl}");
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        output.WriteLine($"Kestrel {client.BaseAddress} /ops/v1/{route}: HTTP {(int)response.StatusCode}; Cache-Control={response.Headers.CacheControl}; dependencyFailed={failedDependency}.");
    }

    private static HealthCheckBindingV1 Check(string name, HealthPurposeV1 purpose, string tag) => new(name, name, purpose, tag,
        DependencyKindV1.Local, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), ProbePolicyV1.ReadOnly, false);
}
