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

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class HealthNegativeMatrixTests
{
    [Fact]
    public async Task StaleCacheRefreshesAndHiddenFailureStillChangesAggregate()
    {
        var state = new ProbeState();
        var builder = Builder(); Register(builder, state);
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = Address(host) };
        var up = await client.GetAsync("/ops/v1/ready"); Assert.Equal(HttpStatusCode.OK, up.StatusCode);
        Assert.DoesNotContain("private-dependency", await up.Content.ReadAsStringAsync());
        state.Fail = true; await Task.Delay(50);
        var down = await client.GetAsync("/ops/v1/ready"); Assert.Equal(HttpStatusCode.ServiceUnavailable, down.StatusCode);
        Assert.DoesNotContain("private-dependency", await down.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ops/v1/startup")).StatusCode); // required startup inventory absent
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        var result = await host.Services.GetRequiredService<HealthRuntimeV1>().ProbeAsync(HealthPurposeV1.Ready, cancelled.Token);
        Assert.False(result.Healthy); Assert.False(result.ReadAvailable);
    }

    [Theory]
    [InlineData("missing-check")]
    [InlineData("wrong-tag")]
    [InlineData("missing-policy")]
    [InlineData("collision")]
    public async Task RegistrationFailsClosed(string failure)
    {
        var builder = Builder();
        if (failure != "missing-policy") builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        else builder.Services.AddAuthorization();
        if (failure != "missing-check") builder.Services.AddHealthChecks().AddCheck("dependency", () => HealthCheckResult.Healthy(), tags: [failure == "wrong-tag" ? "wrong" : "ready-v1"]);
        builder.Services.AddCp6HealthV1(Profile(), new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build();
        if (failure == "collision") host.MapGet("/ops/{**path}", () => "collision");
        Assert.Throws<InvalidOperationException>(() => host.MapCp6HealthV1());
    }

    [Fact]
    public async Task LaterRouteCollisionFailsAtHostStart()
    {
        var builder = Builder(); Register(builder, new ProbeState());
        await using var host = builder.Build(); host.MapCp6HealthV1(); host.MapGet("/ops/v1/ready", () => "collision");
        await Assert.ThrowsAnyAsync<Exception>(() => host.StartAsync());
    }

    [Fact]
    public async Task ExceptionsAndCancelledCheckResultsAreSafeAndNeverHealthy()
    {
        foreach (var cancelled in new[] { true, false })
        {
            var state = new ProbeState { Cancel = cancelled, Throw = !cancelled };
            var builder = Builder(); Register(builder, state);
            await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
            using var client = new HttpClient { BaseAddress = Address(host) };
            var response = await client.GetAsync("/ops/v1/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.DoesNotContain("SYNTHETIC", await response.Content.ReadAsStringAsync());
        }
    }

    private static HealthProfileV1 Profile() => new("operator", [new HealthCheckBindingV1("dependency", "private-dependency", HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.Database, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(20), ProbePolicyV1.ReadOnly, false)], [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["dependency"])]);
    private static void Register(WebApplicationBuilder builder, ProbeState state)
    {
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("dependency", state, tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(Profile(), new RuntimeIdentityEvaluatorV1(null, null));
    }
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); return builder;
    }
    private static Uri Address(WebApplication host) => new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
    private sealed class ProbeState : IHealthCheck
    {
        internal bool Fail, Cancel, Throw;
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (Throw) throw new InvalidOperationException("SYNTHETIC-password-sql-path");
            if (Cancel) throw new OperationCanceledException("SYNTHETIC-secret");
            return Task.FromResult(Fail ? HealthCheckResult.Unhealthy("SYNTHETIC-secret") : HealthCheckResult.Healthy());
        }
    }
}
