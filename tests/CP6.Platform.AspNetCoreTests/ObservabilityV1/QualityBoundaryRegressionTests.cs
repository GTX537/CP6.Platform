using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Net;
using CP6.Platform.Abstractions;
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

[Collection(nameof(OpenTelemetryGlobalStateCollection))]
public sealed class QualityBoundaryRegressionTests
{
    [Fact]
    public async Task NullAndOversizedMetricDimensionsCannotThrowFromActualCounterOrChangeRequestResult()
    {
        var policy = new TelemetryPolicyV1("orders", "test", "east", ["/write"]);
        var sink = new LocalTelemetrySinkV1(); var builder = Builder();
        builder.Services.AddCp6ObservabilityV1(policy, sink);
        using var meter = new Meter(Cp6TelemetryMeters.Messaging);
        var counter = meter.CreateCounter<long>("cp6.messaging.published");
        await using var host = builder.Build();
        host.MapGet("/write", () =>
        {
            counter.Add(1, new KeyValuePair<string, object?>(null!, "success"));
            counter.Add(1, new KeyValuePair<string, object?>(new string('k', 100_000), "success"));
            counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", new string('v', 100_000)));
            return "owner-result-unchanged";
        });
        await host.StartAsync(); using var client = Client(host);
        using var response = await client.GetAsync("/write");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("owner-result-unchanged", await response.Content.ReadAsStringAsync());
        await host.Services.GetRequiredService<ObservabilityRuntimeV1>().FlushAsync(TimeSpan.FromSeconds(1));
        var metric = Assert.Single(sink.Snapshot(), record => record.Name == "cp6.messaging.published");
        Assert.Equal(3, metric.Value); Assert.Empty(metric.Attributes);
        Assert.False(policy.Allows(null!, "success"));
        Assert.False(policy.Allows(new string('k', 100_000), "success"));
        Assert.False(policy.Allows("cp6.outcome", new string('v', 100_000)));
    }

    [Fact]
    public async Task ActualLoggerCallsRacingRuntimeDisposalNeverThrowBackToCaller()
    {
        var failures = new ConcurrentQueue<Exception>();
        for (var lifecycle = 0; lifecycle < 5; lifecycle++)
        {
            var runtime = new ObservabilityRuntimeV1(new TelemetryPolicyV1("orders", "test", "east", []), new LocalTelemetrySinkV1(4), new TelemetryLimitsV1(4, 8, 2, 32, 4));
            using var provider = new PrivacyLoggerProviderV1(runtime); var logger = provider.CreateLogger("caller");
            using var started = new CountdownEvent(4);
            var writers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                started.Signal();
                for (var call = 0; call < 5000; call++)
                    try { logger.LogWarning("SYNTHETIC-sensitive-argument {Value}", call); }
                    catch (Exception error) { failures.Enqueue(error); }
            })).ToArray();
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            await runtime.DisposeAsync();
            await Task.WhenAll(writers);
            logger.LogWarning("post-dispose");
            Assert.True(runtime.DroppedCount > 0);
        }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task HealthCacheExpiresByMonotonicTimeEvenWhenUtcMovesBackwards()
    {
        var clock = new SplitClock(); var calls = 0; var fail = false; var builder = Builder();
        builder.Services.AddSingleton<TimeProvider>(clock);
        builder.Services.AddAuthorization(options => options.AddPolicy("operator", policy => policy.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("store", () => { Interlocked.Increment(ref calls); return fail ? HealthCheckResult.Unhealthy() : HealthCheckResult.Healthy(); }, tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(new HealthProfileV1("operator",
            [new HealthCheckBindingV1("store", "store", HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.Local, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1), ProbePolicyV1.ReadOnly, true)],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["store"])]), new RuntimeIdentityEvaluatorV1(null, null));
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync(); using var client = Client(host);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ops/v1/ready")).StatusCode); Assert.Equal(1, calls);
        fail = true; clock.UtcNow -= TimeSpan.FromHours(2); clock.Elapsed += TimeSpan.FromSeconds(10);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ops/v1/ready")).StatusCode); Assert.Equal(1, calls);
        clock.Elapsed += TimeSpan.FromMinutes(1);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/ops/v1/ready")).StatusCode); Assert.Equal(2, calls);
    }

    private sealed class SplitClock : TimeProvider
    {
        internal DateTimeOffset UtcNow = DateTimeOffset.UtcNow;
        internal TimeSpan Elapsed;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => Elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders(); builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); return builder;
    }
    private static HttpClient Client(WebApplication host) => new() { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
}
