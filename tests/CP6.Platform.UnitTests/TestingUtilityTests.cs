using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using CP6.Platform.Abstractions;
using CP6.Platform.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CP6.Platform.UnitTests;

[Collection(nameof(TestingUtilityCollection))]
public sealed class TestingUtilityTests
{
    [Fact]
    public async Task Recorder_CapturesConcurrentActivitiesAndMetricsInSequence()
    {
        const string sourceName = "CP6.Platform.Testing.Concurrent";
        using var source = new ActivitySource(sourceName);
        using var meter = new Meter(sourceName);
        var counter = meter.CreateCounter<long>("cp6.testing.concurrent");
        using var recorder = new Cp6TelemetryRecorder([sourceName], [sourceName]);

        await Task.WhenAll(Enumerable.Range(0, 32).Select(index => Task.Run(() =>
        {
            using var activity = source.StartActivity("concurrent", ActivityKind.Internal);
            activity?.SetTag(Cp6TelemetryConventions.OperationTag, "concurrent");
            counter.Add(1, new KeyValuePair<string, object?>(Cp6TelemetryConventions.OutcomeTag, "success"));
        })));

        var activities = recorder.GetActivities();
        var metrics = recorder.GetMetrics();
        Assert.Equal(32, activities.Count);
        Assert.Equal(32, metrics.Count);
        AssertStrictlyIncreasing(activities.Select(item => item.Sequence));
        AssertStrictlyIncreasing(metrics.Select(item => item.Sequence));
        Assert.All(activities, item => Assert.Equal("concurrent", item.Tags[Cp6TelemetryConventions.OperationTag]));
        Assert.All(metrics, item => Assert.Equal("success", item.Tags[Cp6TelemetryConventions.OutcomeTag]));
    }

    [Fact]
    public void Recorder_ValidatesTraceTopologyAndAllowedTags()
    {
        const string sourceName = "CP6.Platform.Testing.Topology";
        using var source = new ActivitySource(sourceName);
        using var recorder = new Cp6TelemetryRecorder([sourceName], []);

        using (var root = source.StartActivity("service-a.server", ActivityKind.Server))
        {
            root?.SetTag(Cp6TelemetryConventions.OperationTag, "service-a.server");
            using (var client = source.StartActivity("service-a.client", ActivityKind.Client))
            {
                client?.SetTag(Cp6TelemetryConventions.OutcomeTag, "success");
                using var server = source.StartActivity("service-b.server", ActivityKind.Server);
                server?.SetTag(Cp6TelemetryConventions.OutcomeTag, "success");
            }
        }

        var traceId = recorder.AssertTraceTopology(
            "service-a.server",
            "service-a.client",
            "service-b.server");

        Assert.NotEqual(default, traceId);
        recorder.EnsureOnlyAllowedTags(Cp6TelemetryConventions.AllowedMetricTags);
        Assert.Throws<InvalidOperationException>(() => recorder.AssertTraceTopology("service-b.server", "service-a.client"));
    }

    [Fact]
    public void Recorder_RejectsForbiddenTagNamesAndValues()
    {
        const string sourceName = "CP6.Platform.Testing.Forbidden";
        using var source = new ActivitySource(sourceName);
        using var recorder = new Cp6TelemetryRecorder([sourceName], []);
        using (var activity = source.StartActivity("operation"))
        {
            activity?.SetTag("cp6.unsafe", "password=secret-value");
        }

        Assert.Throws<InvalidOperationException>(() => recorder.ThrowIfContainsForbiddenData("password", "secret-value"));
        Assert.Throws<InvalidOperationException>(() => recorder.EnsureOnlyAllowedTags(Cp6TelemetryConventions.AllowedMetricTags));
    }

    [Fact]
    public void Recorder_DisposeStopsBothListeners()
    {
        const string sourceName = "CP6.Platform.Testing.Disposed";
        using var source = new ActivitySource(sourceName);
        using var meter = new Meter(sourceName);
        var recorder = new Cp6TelemetryRecorder([sourceName], [sourceName]);
        recorder.Dispose();

        using var activity = source.StartActivity("after-dispose");
        meter.CreateCounter<long>("after-dispose").Add(1);

        Assert.Empty(recorder.GetActivities());
        Assert.Empty(recorder.GetMetrics());
    }

    [Fact]
    public async Task FaultScript_ExecutesStatusExceptionDelayAndSuccessExactlyOnce()
    {
        var supplied = new[]
        {
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Throw(new HttpRequestException("synthetic")),
            Cp6HttpFaultOutcome.Delay(TimeSpan.FromMilliseconds(1)),
            Cp6HttpFaultOutcome.Success
        };
        var script = new Cp6HttpFaultScript(supplied);
        supplied[0] = Cp6HttpFaultOutcome.Success;
        var transport = new RecordingHandler();
        using var handler = new Cp6HttpFaultHandler(script) { InnerHandler = transport };
        using var invoker = new HttpMessageInvoker(handler);

        using var first = await invoker.SendAsync(Request(), CancellationToken.None);
        await Assert.ThrowsAsync<HttpRequestException>(() => invoker.SendAsync(Request(), CancellationToken.None));
        using var third = await invoker.SendAsync(Request(), CancellationToken.None);
        using var fourth = await invoker.SendAsync(Request(), CancellationToken.None);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, third.StatusCode);
        Assert.Equal(HttpStatusCode.OK, fourth.StatusCode);
        Assert.Equal(4, script.AttemptCount);
        Assert.Equal(2, transport.AttemptCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => invoker.SendAsync(Request(), CancellationToken.None));
        Assert.Equal(5, script.AttemptCount);
    }

    [Fact]
    public async Task FaultDelay_PreservesCallerCancellationAndSkipsTransport()
    {
        var script = new Cp6HttpFaultScript([Cp6HttpFaultOutcome.Delay(TimeSpan.FromMinutes(1))]);
        var transport = new RecordingHandler();
        using var handler = new Cp6HttpFaultHandler(script) { InnerHandler = transport };
        using var invoker = new HttpMessageInvoker(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invoker.SendAsync(Request(), cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, script.AttemptCount);
        Assert.Equal(0, transport.AttemptCount);
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("test")]
    public void FaultInjection_RejectsNonTestEnvironmentBeforeChangingServices(string environment)
    {
        var services = new ServiceCollection();
        var host = new HostEnvironmentStub(environment);

        Assert.Throws<InvalidOperationException>(
            () => services.AddCp6HttpFaultInjection(host, new Cp6HttpFaultScript([Cp6HttpFaultOutcome.Success])));

        Assert.Empty(services);
    }

    [Theory]
    [InlineData("Test")]
    [InlineData("CI")]
    public void FaultInjection_AcceptsOnlyExactApprovedEnvironments(string environment)
    {
        var services = new ServiceCollection();
        var script = new Cp6HttpFaultScript([Cp6HttpFaultOutcome.Success]);

        services.AddCp6HttpFaultInjection(new HostEnvironmentStub(environment), script);
        using var provider = services.BuildServiceProvider();

        Assert.Same(script, provider.GetRequiredService<Cp6HttpFaultScript>());
        Assert.NotSame(
            provider.GetRequiredService<Cp6HttpFaultHandler>(),
            provider.GetRequiredService<Cp6HttpFaultHandler>());
    }

    private static HttpRequestMessage Request() => new(HttpMethod.Get, "https://example.test/resource");

    private static void AssertStrictlyIncreasing(IEnumerable<long> values)
    {
        long? previous = null;
        foreach (var value in values)
        {
            Assert.True(previous is null || value > previous);
            previous = value;
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private int attemptCount;

        public int AttemptCount => Volatile.Read(ref attemptCount);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref attemptCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request });
        }
    }

    private sealed class HostEnvironmentStub(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "CP6.Platform.UnitTests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}

[CollectionDefinition(nameof(TestingUtilityCollection), DisableParallelization = true)]
public sealed class TestingUtilityCollection;
