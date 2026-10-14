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

public sealed class HealthBindingTests(ITestOutputHelper output)
{
    [Fact]
    public async Task RealKestrelDependencyStopRestartAffectsReadinessButNotLiveAndNeverGrantsNewEffects()
    {
        await using var dependency = Builder().Build();
        dependency.MapGet("/probe", () => "ok");
        await dependency.StartAsync();
        var address = Address(dependency);
        using var dependencyClient = new HttpClient { BaseAddress = address };
        var builder = Builder();
        builder.Services.AddAuthorization(options => options.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("dependency", new HttpProbe(dependencyClient), tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(Profile(), new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build();
        host.MapCp6HealthV1();
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = Address(host) };
        var up = await client.GetAsync("/ops/v1/ready");
        var body = await up.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, up.StatusCode); // new effects lack identity evidence
        Assert.Contains("\"readAvailable\":true", body);
        Assert.Contains("\"newEffectsAvailable\":false", body);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ops/v1/live")).StatusCode);
        await dependency.StopAsync();
        await Task.Delay(40);
        var down = await client.GetStringAsync("/ops/v1/live");
        var failed = await client.GetAsync("/ops/v1/ready");
        Assert.Contains("\"readAvailable\":false", await failed.Content.ReadAsStringAsync());
        output.WriteLine($"Kestrel {client.BaseAddress} dependency {address}; up {up.StatusCode}; stopped {failed.StatusCode}; live {down}");
        await using var restarted = Builder(address.Port).Build();
        restarted.MapGet("/probe", () => "ok");
        await restarted.StartAsync();
        await Task.Delay(40);
        string recovered = "";
        for (var attempt = 0; attempt < 20; attempt++)
        {
            recovered = await (await client.GetAsync("/ops/v1/ready")).Content.ReadAsStringAsync();
            if (recovered.Contains("\"readAvailable\":true", StringComparison.Ordinal)) break;
            await Task.Delay(50);
        }
        Assert.Contains("\"readAvailable\":true", recovered);
        output.WriteLine("Dependency restarted on same loopback port; read restored; no Owner writes, migrations or replay configured; hosts disposed.");
    }

    [Fact]
    public void MissingWrongTagAndRouteCollisionsAreRejected()
    {
        var builder = Builder();
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("dependency", () => HealthCheckResult.Healthy(), tags: ["wrong"]);
        builder.Services.AddCp6HealthV1(Profile(), new RuntimeIdentityEvaluatorV1(null, null));
        using var host = builder.Build();
        Assert.Throws<InvalidOperationException>(() => host.MapCp6HealthV1());
        Assert.Throws<ArgumentException>(() => new HealthProfileV1("operator", [], [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["dependency"])]));
    }

    [Fact]
    public async Task BlockedProbeIsBoundedAndNeverHealthyIncludingCancellation()
    {
        var blocker = new BlockingProbe();
        var builder = Builder();
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("dependency", blocker, tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(Profile(), new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build();
        host.MapCp6HealthV1();
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = Address(host), Timeout = TimeSpan.FromSeconds(3) };
        for (var i = 0; i < 4; i++) Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ops/v1/ready")).StatusCode);
        Assert.Equal(1, blocker.Calls);
        blocker.Release.SetResult();
    }

    private static HealthProfileV1 Profile() => new("operator",
        [new HealthCheckBindingV1("dependency", "dependency", HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.RemoteService, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), ProbePolicyV1.ReadOnly, false)],
        [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["dependency"]), new CapabilityRouteV1("/write", CapabilityV1.NewEffect, "CP6.Platform.Contracts", ["dependency"])]);
    private static WebApplicationBuilder Builder(int port = 0)
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, port)); return builder;
    }
    private static Uri Address(WebApplication host) => new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    private sealed class HttpProbe(HttpClient client) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
            (await client.GetAsync("/probe", cancellationToken)).IsSuccessStatusCode ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy();
    }
    private sealed class BlockingProbe : IHealthCheck
    {
        internal int Calls;
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Calls); Release.Task.GetAwaiter().GetResult(); return Task.FromResult(HealthCheckResult.Healthy());
        }
    }
}
