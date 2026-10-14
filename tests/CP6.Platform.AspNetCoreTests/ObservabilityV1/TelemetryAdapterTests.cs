using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using Xunit.Abstractions;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

[Collection(nameof(OpenTelemetryGlobalStateCollection))]
public sealed class TelemetryAdapterTests(ITestOutputHelper output)
{
    private const string Secret = "SYNTHETIC-private-login-code-tenant-user-message-sql";

    [Fact]
    public async Task ActualSdkFinalTraceMetricAndLogExportsContainOnlyClosedSafeFields()
    {
        var builder = Builder();
        var sink = new LocalTelemetrySinkV1(128);
        builder.Services.AddCp6ObservabilityV1(Policy(), sink);
        await using var host = builder.Build();
        host.MapGet("/orders/{id}", async (HttpContext context, ILogger<TelemetryAdapterTests> logger) =>
        {
            using var source = new ActivitySource(Cp6TelemetrySources.AspNetCore);
            using var activity = source.StartActivity(Secret);
            activity?.SetTag("http.route", Secret); activity?.SetTag("cp6.outcome", Secret);
            activity?.SetTag("Authorization", Secret); activity?.SetBaggage("secret", Secret);
            activity?.AddEvent(new ActivityEvent(Secret, tags: new ActivityTagsCollection { { "exception.message", Secret } }));
            activity?.SetStatus(ActivityStatusCode.Error, Secret);
            using var scope = logger.BeginScope(new Dictionary<string, object> { ["password"] = Secret });
            logger.LogError(new InvalidOperationException(Secret), "Private {Code}", Secret);
            using var meter = new Meter(Cp6TelemetryMeters.Messaging);
            var counter = meter.CreateCounter<long>("cp6.messaging.published");
            counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", "success"), new("tenant.id", Secret), new("cp6.operation", Secret));
            using var client = new HttpClient();
            using var downstream = await client.GetAsync(new Uri(Address(host), "/target?code=" + Secret));
            downstream.EnsureSuccessStatusCode();
            return Results.Ok();
        });
        host.MapGet("/target", (HttpContext context) =>
        {
            Assert.True(context.Request.Headers.ContainsKey("traceparent"));
            Assert.False(context.Request.Headers.ContainsKey("baggage"));
            return Results.Ok();
        });
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = Address(host) };
        using var request = new HttpRequestMessage(HttpMethod.Get, "/orders/" + Secret + "?code=" + Secret);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Secret);
        request.Headers.TryAddWithoutValidation("Cookie", "session=" + Secret);
        request.Headers.TryAddWithoutValidation("baggage", "secret=" + Secret);
        request.Content = new StringContent(Secret);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var runtime = host.Services.GetRequiredService<ObservabilityRuntimeV1>();
        await runtime.FlushAsync(TimeSpan.FromSeconds(2));
        var records = sink.Snapshot();
        var json = JsonSerializer.Serialize(records);
        Assert.DoesNotContain(Secret, json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(records, r => r.Signal == TelemetrySignalV1.Trace && r.Name == "http.server");
        Assert.Contains(records, r => r.Signal == TelemetrySignalV1.Trace && r.Name == "http.client");
        Assert.Contains(records, r => r.Signal == TelemetrySignalV1.Log);
        Assert.Contains(records, r => r.Signal == TelemetrySignalV1.Metric && r.Name == "cp6.messaging.published" && r.Value == 1);
        Assert.All(records, r => Assert.All(r.Attributes, a => Assert.True(Policy().Allows(a.Key, a.Value))));
        Assert.All(records, r => Assert.Equal(3, r.Resources.Count));
        output.WriteLine($"Kestrel {client.BaseAddress}, HTTP {(int)response.StatusCode}; SDK final sink: {json}");
    }

    [Fact]
    public async Task BlockingAndThrowingSinkCannotDelayOwnerResultAndQueueDropsAreExplicit()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new LocalTelemetrySinkV1(8) { BeforeWrite = _ => { release.Task.GetAwaiter().GetResult(); } };
        var builder = Builder();
        builder.Services.AddCp6ObservabilityV1(Policy(), sink, new TelemetryLimitsV1(queueCapacity: 4, maximumRecords: 8, batchSize: 2, recordsPerSecond: 256, maximumSeries: 8));
        await using var host = builder.Build();
        var commits = 0;
        host.MapGet("/target", (ILogger<TelemetryAdapterTests> logger) => { for (var i = 0; i < 100; i++) logger.LogWarning("{Secret}", Secret); return Interlocked.Increment(ref commits); });
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = Address(host), Timeout = TimeSpan.FromSeconds(3) };
        Assert.Equal("1", await client.GetStringAsync("/target"));
        var runtime = host.Services.GetRequiredService<ObservabilityRuntimeV1>();
        Assert.True(runtime.DroppedCount > 0);
        Assert.Equal(TelemetryCompletenessV1.Incomplete, runtime.Completeness);
        Assert.False(await runtime.FlushAsync(TimeSpan.FromMilliseconds(50)));
        sink.BeforeWrite = _ => throw new InvalidOperationException(Secret);
        release.TrySetResult();
        Assert.Equal("2", await client.GetStringAsync("/target"));
        output.WriteLine($"Owner fixture response count={commits}; bounded queue dropped={runtime.DroppedCount}; completeness={runtime.Completeness}.");
    }

    [Fact]
    public async Task MultipleHostsUseSeparateResourcesAndRestoreOnlyOwnedGlobalPropagators()
    {
        var original = DistributedContextPropagator.Current;
        var originalOtel = Propagators.DefaultTextMapPropagator;
        var firstBuilder = Builder(); var firstSink = new LocalTelemetrySinkV1(32);
        firstBuilder.Services.AddCp6ObservabilityV1(Policy(), firstSink);
        await using var first = firstBuilder.Build(); first.MapGet("/target", () => "one"); await first.StartAsync();
        var secondBuilder = Builder(); var secondSink = new LocalTelemetrySinkV1(32);
        secondBuilder.Services.AddCp6ObservabilityV1(new TelemetryPolicyV1("other", "test", "east", ["/target"]), secondSink);
        await using (var second = secondBuilder.Build())
        {
            second.MapGet("/target", () => "two"); await second.StartAsync();
            using var client = new HttpClient();
            Assert.Equal("one", await client.GetStringAsync(new Uri(Address(first), "/target")));
            Assert.Equal("two", await client.GetStringAsync(new Uri(Address(second), "/target")));
            await first.Services.GetRequiredService<ObservabilityRuntimeV1>().FlushAsync(TimeSpan.FromSeconds(1));
            await second.Services.GetRequiredService<ObservabilityRuntimeV1>().FlushAsync(TimeSpan.FromSeconds(1));
            Assert.All(firstSink.Snapshot(), r => Assert.Equal("orders", r.Resources["service.name"]));
            Assert.All(secondSink.Snapshot(), r => Assert.Equal("other", r.Resources["service.name"]));
            Assert.Single(firstSink.Snapshot(), r => r.Name == "http.server");
            Assert.Single(secondSink.Snapshot(), r => r.Name == "http.server");
        }
        var replacement = DistributedContextPropagator.CreateDefaultPropagator();
        DistributedContextPropagator.Current = replacement;
        Assert.False(first.Services.GetRequiredService<ObservabilityRuntimeV1>().PropagationIntact);
        await first.DisposeAsync();
        Assert.Same(replacement, DistributedContextPropagator.Current);
        DistributedContextPropagator.Current = original;
        Sdk.SetDefaultTextMapPropagator(originalOtel);
    }

    private static TelemetryPolicyV1 Policy() => new("orders", "test", "east", ["/orders/{id}", "/target"]);
    private static WebApplicationBuilder Builder()
    {
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); return builder;
    }
    private static Uri Address(WebApplication host) => new(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
}
