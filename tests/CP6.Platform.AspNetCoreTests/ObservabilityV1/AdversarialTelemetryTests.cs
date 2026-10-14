using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts.ObservabilityV1;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

[Collection(nameof(OpenTelemetryGlobalStateCollection))]
public sealed class AdversarialTelemetryTests
{
    [Fact]
    public async Task EventLinkBaggageStatusDisplayNameAndCaseVariantSeedsNeverEnterFinalSink()
    {
        const string secret = "SYNTHETIC-secret-user-tenant-operation-body-sql-login";
        var sink = new LocalTelemetrySinkV1();
        await using var runtime = Runtime(sink);
        using (runtime.BeginScope())
        using (var source = new ActivitySource(Cp6TelemetrySources.Messaging))
        {
            var link = new ActivityLink(new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded),
                new ActivityTagsCollection { { "link.secret", secret } });
            using var span = source.StartActivity(secret, ActivityKind.Producer, default(ActivityContext),
                tags: new[] { new KeyValuePair<string, object?>("url.full", secret), new("cp6.outcome", secret), new("cp6.operation", secret), new("HTTP.ROUTE", secret), new("authorization", secret), new("Cookie", secret), new("sql.parameters", secret), new("tenant.id", secret) }, links: [link]);
            Assert.NotNull(span);
            span.SetBaggage("baggage", secret); span.DisplayName = secret;
            span.AddEvent(new ActivityEvent(secret, tags: new ActivityTagsCollection { { "exception.stacktrace", secret } }));
            span.SetStatus(ActivityStatusCode.Error, secret);
        }
        await runtime.FlushAsync(TimeSpan.FromSeconds(1));
        var record = Assert.Single(sink.Snapshot(), r => r.Signal == TelemetrySignalV1.Trace);
        Assert.Empty(record.Attributes);
        Assert.DoesNotContain(secret, JsonSerializer.Serialize(record), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("telemetry.sdk", JsonSerializer.Serialize(record), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(3, record.Resources.Count);
    }

    [Fact]
    public async Task MetricsAreSanitizedBeforeSdkAggregationAndSeriesAndRecordBudgetsAreEnforced()
    {
        var sink = new LocalTelemetrySinkV1(8);
        await using var runtime = Runtime(sink, new TelemetryLimitsV1(4, 8, 2, 4, 2));
        using (runtime.BeginScope())
        using (var meter = new Meter(Cp6TelemetryMeters.Messaging))
        {
            var counter = meter.CreateCounter<long>("cp6.messaging.published");
            counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", "success"), new("user.id", new Poison()));
            counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", "failure"));
            counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", "cancelled")); // third series rejected
            counter.Add(1, Enumerable.Range(0, 33).Select(i => new KeyValuePair<string, object?>("raw" + i, new Poison())).ToArray());
            for (var i = 0; i < 20; i++) counter.Add(1, new KeyValuePair<string, object?>("cp6.outcome", "success"));
        }
        await runtime.FlushAsync(TimeSpan.FromSeconds(1));
        var metrics = sink.Snapshot().Where(r => r.Signal == TelemetrySignalV1.Metric).ToArray();
        Assert.InRange(metrics.Length, 1, 2);
        Assert.True(runtime.DroppedCount >= 2);
        // Measurement rate must be applied before aggregation, not only to the exported aggregate.
        Assert.True(metrics.Sum(r => r.Value) <= 4);
        Assert.All(metrics, r => Assert.True(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r)) <= 4096));
    }

    [Fact]
    public async Task LoggerNeverInvokesCallerFormatterStateScopeOrExceptionFormatting()
    {
        var sink = new LocalTelemetrySinkV1();
        await using var runtime = Runtime(sink);
        using var provider = new PrivacyLoggerProviderV1(runtime);
        var logger = provider.CreateLogger("SYNTHETIC-private-category");
        using var scope = logger.BeginScope(new Poison());
        logger.Log(LogLevel.Error, new EventId(123, "SYNTHETIC-secret"), new Poison(), new InvalidOperationException("SYNTHETIC-secret"), (_, _) => throw new InvalidOperationException("Formatter executed"));
        await runtime.FlushAsync(TimeSpan.FromSeconds(1));
        var record = Assert.Single(sink.Snapshot());
        Assert.Equal(TelemetrySignalV1.Log, record.Signal);
        Assert.Equal("Error", record.Attributes["log.severity"]);
        Assert.DoesNotContain("SYNTHETIC", JsonSerializer.Serialize(record));
    }

    [Fact]
    public async Task RepeatedLifecycleRestoresGlobalsAndOtelReplacementDisablesExportWithoutClobberingReplacement()
    {
        var previous = DistributedContextPropagator.Current; var previousOtel = Propagators.DefaultTextMapPropagator;
        for (var i = 0; i < 3; i++)
        {
            await using (var runtime = Runtime(new LocalTelemetrySinkV1())) Assert.True(runtime.PropagationIntact);
            Assert.Same(previous, DistributedContextPropagator.Current); Assert.Same(previousOtel, Propagators.DefaultTextMapPropagator);
        }
        var sink = new LocalTelemetrySinkV1();
        await using (var runtime = Runtime(sink))
        {
            var replacement = new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()]);
            Sdk.SetDefaultTextMapPropagator(replacement);
            Assert.False(runtime.PropagationIntact);
            runtime.Log(LogLevel.Error);
            Assert.True(runtime.DroppedCount > 0); Assert.Empty(sink.Snapshot());
            await runtime.DisposeAsync();
            Assert.Same(replacement, Propagators.DefaultTextMapPropagator);
        }
        Sdk.SetDefaultTextMapPropagator(previousOtel);
    }

    [Fact]
    public async Task SynchronousSinkBlockAcrossRepeatedDisposalsIsGloballyBounded()
    {
        using var release = new ManualResetEventSlim();
        var entered = 0;
        try
        {
            for (var i = 0; i < 6; i++)
            {
                var sink = new LocalTelemetrySinkV1(1) { BeforeWrite = _ => { Interlocked.Increment(ref entered); release.Wait(); } };
                var runtime = Runtime(sink);
                runtime.Log(LogLevel.Warning);
                await Task.Delay(20);
                await runtime.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1));
            }
            Assert.InRange(entered, 1, 4);
        }
        finally { release.Set(); await Task.Delay(100); }
    }

    [Fact]
    public void DuplicateAndDriftedRegistrationAreRejected()
    {
        var services = new ServiceCollection();
        services.AddCp6ObservabilityV1(Policy(), new LocalTelemetrySinkV1());
        Assert.Throws<InvalidOperationException>(() => services.AddCp6ObservabilityV1(Policy(), new LocalTelemetrySinkV1()));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TelemetryLimitsV1(batchSize: 257));
    }

    [Fact]
    public async Task LocalQueueBoundsBatchLifetimeRecordCountRetentionSizeAndPostDisposeWrites()
    {
        var largestBatch = 0;
        var sink = new LocalTelemetrySinkV1(2) { BeforeWrite = batch => largestBatch = Math.Max(largestBatch, batch.Count) };
        var queue = new SafeExportQueueV1(sink, new TelemetryLimitsV1(4, 7, 3, 256, 8));
        var record = new SafeTelemetryRecordV1(TelemetrySignalV1.Log, "log.event", 0, 1,
            Policy().Sanitize([new("log.severity", "Error")]), Policy());
        var accepted = 0;
        for (var i = 0; i < 100; i++) if (queue.TryEnqueue(record)) accepted++;
        Assert.InRange(accepted, 1, 7);
        Assert.False(await queue.FlushAsync(TimeSpan.FromSeconds(1))); // explicit incomplete due to drops
        Assert.InRange(largestBatch, 1, 3);
        Assert.InRange(sink.Snapshot().Count, 1, 2);
        Assert.True(queue.Dropped >= 93);
        Assert.All(sink.Snapshot(), r => Assert.InRange(System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(r)), 1, 4096));
        await queue.DisposeAsync();
        Assert.False(queue.TryEnqueue(record));
    }

    private static TelemetryPolicyV1 Policy() => new("orders", "test", "east", ["/orders/{id}"]);
    private static ObservabilityRuntimeV1 Runtime(LocalTelemetrySinkV1 sink, TelemetryLimitsV1? limits = null) => new(Policy(), sink, limits ?? new TelemetryLimitsV1());
    private sealed class Poison { public override string ToString() => throw new InvalidOperationException("User formatting must not run."); }
}
