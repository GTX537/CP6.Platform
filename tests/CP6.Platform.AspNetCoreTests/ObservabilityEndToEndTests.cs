using System.Diagnostics;
using System.Net;
using System.Text.Json;
using CP6.Platform.Abstractions;
using CP6.Platform.AspNetCore;
using CP6.Platform.Testing;

namespace CP6.Platform.AspNetCoreTests;

[Collection(nameof(OpenTelemetryGlobalStateCollection))]
public sealed class ObservabilityEndToEndTests
{
    [Fact]
    public async Task TwoHosts_ProduceOneW3cTraceWithIndependentCorrelationAndSafeResources()
    {
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync();

        var response = await fixture.SendRawAsync(
            HttpMethod.Get,
            "/proxy/read",
            ("X-Correlation-Id", "business-correlation"));
        var body = JsonSerializer.Deserialize<ProxyResponse>(response.Body, JsonOptions());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("business-correlation", body.CorrelationId);
        var chain = TraceChain(fixture.Recorder);
        Assert.Equal(chain.ServerA.TraceId, chain.ClientA.TraceId);
        Assert.Equal(chain.ServerA.TraceId, chain.ServerB.TraceId);
        Assert.Equal(chain.ServerA.SpanId, chain.ClientA.ParentSpanId);
        Assert.Equal(chain.ClientA.SpanId, chain.ServerB.ParentSpanId);
        Assert.Equal(3, new[] { chain.ServerA.SpanId, chain.ClientA.SpanId, chain.ServerB.SpanId }.Distinct().Count());
        Assert.NotEqual(body.CorrelationId, chain.ServerA.TraceId.ToString());
        Assert.Empty(chain.ServerA.Baggage);
        Assert.Empty(chain.ClientA.Baggage);
        Assert.Empty(chain.ServerB.Baggage);
        AssertResource(fixture.ServiceAResource, "cp6-test-service-a");
        AssertResource(fixture.ServiceBResource, "cp6-test-service-b");
        fixture.Recorder.EnsureOnlyAllowedTags(Cp6TelemetryConventions.AllowedMetricTags);
    }

    [Fact]
    public async Task InvalidAndDuplicateTraceHeaders_CreateFreshTraceRoots()
    {
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync();
        var knownTraceIds = new[]
        {
            "11111111111111111111111111111111",
            "33333333333333333333333333333333"
        };
        var cases = new[]
        {
            new[] { ("traceparent", "malformed-parent") },
            new[]
            {
                ("traceparent", $"00-{knownTraceIds[0]}-2222222222222222-01"),
                ("traceparent", $"00-{knownTraceIds[1]}-4444444444444444-01")
            },
            new[] { ("traceparent", $"00-{knownTraceIds[0]}-2222222222222222-01-extra") }
        };

        foreach (var headers in cases)
        {
            var before = fixture.Recorder.GetActivities().Select(activity => activity.Sequence).DefaultIfEmpty().Max();
            var response = await fixture.SendRawAsync(HttpMethod.Get, "/proxy/read", headers);
            var server = FindServer(fixture.Recorder, "/proxy/read", before);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.DoesNotContain(server.TraceId.ToString(), knownTraceIds);
            Assert.Equal(default, server.ParentSpanId);
        }
    }

    [Fact]
    public async Task SpoofedIdentityAndBaggageNeverBecomeTelemetryData()
    {
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync();

        var response = await fixture.SendRawAsync(
            HttpMethod.Get,
            "/proxy/read",
            ("X-Tenant-Id", "secret-tenant"),
            ("X-User-Id", "secret-user"),
            ("baggage", "unsafe=secret-baggage"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        fixture.Recorder.ThrowIfContainsForbiddenData("secret-tenant", "secret-user", "secret-baggage");
        fixture.Recorder.EnsureOnlyAllowedTags(Cp6TelemetryConventions.AllowedMetricTags);
        Assert.All(TraceChain(fixture.Recorder).Activities, activity => Assert.Empty(activity.Baggage));
    }

    [Fact]
    public async Task TransportFailure_ReturnsFailureWithoutFallbackSuccess()
    {
        var script = new Cp6HttpFaultScript(
            Cp6HttpFaultOutcome.Throw(new HttpRequestException("dependency unavailable 1")),
            Cp6HttpFaultOutcome.Throw(new HttpRequestException("dependency unavailable 2")),
            Cp6HttpFaultOutcome.Throw(new HttpRequestException("dependency unavailable 3")));
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync(new() { FaultScript = script });

        var response = await fixture.SendRawAsync(HttpMethod.Get, "/proxy/read");
        var body = JsonSerializer.Deserialize<ProxyResponse>(response.Body, JsonOptions());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.False(body.Success);
        Assert.Equal("downstream_unavailable", body.ErrorCode);
        Assert.Equal(3, script.AttemptCount);
        Assert.Equal(0, fixture.ServiceBRequestCount);
    }

    [Fact]
    public async Task CallerCancellation_StopsFaultDelayAndPreservesCancellation()
    {
        var script = new Cp6HttpFaultScript([Cp6HttpFaultOutcome.Delay(TimeSpan.FromMinutes(1))]);
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync(new() { FaultScript = script });
        using var cancellation = new CancellationTokenSource();

        var request = fixture.Client.GetAsync("/proxy/read", cancellation.Token);
        await fixture.WaitForFaultAttemptsAsync(1);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => request);
        Assert.Equal(1, script.AttemptCount);
        Assert.Equal(0, fixture.ServiceBRequestCount);
    }

    [Fact]
    public async Task ThrowingBatchExporter_CannotChangeBusinessResponse()
    {
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync(new() { UseThrowingExporter = true });

        var response = await fixture.SendRawAsync(HttpMethod.Get, "/proxy/read");
        await fixture.WaitForExporterAttemptAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(fixture.ThrowingExporterAttempts > 0);
    }

    [Fact]
    public async Task IdempotentRead_RetriesTwoScriptedFailuresThenSucceeds()
    {
        var script = new Cp6HttpFaultScript(
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Success);
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync(new() { FaultScript = script });

        var response = await fixture.SendRawAsync(HttpMethod.Get, "/proxy/read");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, script.AttemptCount);
        Assert.Equal(1, fixture.ServiceBRequestCount);
    }

    [Fact]
    public async Task NonIdempotentAndMissingKey_FailClosedBeforeExtraTransportAttempts()
    {
        var nonIdempotentScript = new Cp6HttpFaultScript(
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Success);
        await using (var nonIdempotent = await TwoServiceObservabilityFixture.StartAsync(new()
        {
            OperationKind = Cp6HttpOperationKind.NonIdempotent,
            FaultScript = nonIdempotentScript
        }))
        {
            var response = await nonIdempotent.SendRawAsync(HttpMethod.Post, "/proxy/write");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal(1, nonIdempotentScript.AttemptCount);
        }

        var missingKeyScript = new Cp6HttpFaultScript([Cp6HttpFaultOutcome.Success]);
        await using var missingKey = await TwoServiceObservabilityFixture.StartAsync(new()
        {
            OperationKind = Cp6HttpOperationKind.IdempotentWrite,
            FaultScript = missingKeyScript
        });

        var missingKeyResponse = await missingKey.SendRawAsync(HttpMethod.Post, "/proxy/write");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, missingKeyResponse.StatusCode);
        Assert.Equal(0, missingKeyScript.AttemptCount);
        Assert.Equal(0, missingKey.ServiceBRequestCount);
        Assert.Contains("IdempotencyRequired", missingKeyResponse.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Circuit_OpensWithoutTransportAndRecoversOnManualClock()
    {
        var clock = new ManualTimeProvider();
        var script = new Cp6HttpFaultScript(
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable),
            Cp6HttpFaultOutcome.Success,
            Cp6HttpFaultOutcome.Success);
        await using var fixture = await TwoServiceObservabilityFixture.StartAsync(new()
        {
            OperationKind = Cp6HttpOperationKind.NonIdempotent,
            FaultScript = script,
            TimeProvider = clock,
            CircuitMinimumThroughput = 2,
            CircuitBreakDuration = TimeSpan.FromSeconds(1)
        });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await fixture.SendRawAsync(HttpMethod.Post, "/proxy/write")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await fixture.SendRawAsync(HttpMethod.Post, "/proxy/write")).StatusCode);
        var open = await fixture.SendRawAsync(HttpMethod.Post, "/proxy/write");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, open.StatusCode);
        Assert.Equal(2, script.AttemptCount);
        Assert.Contains("CircuitOpen", open.Body, StringComparison.Ordinal);

        clock.Advance(TimeSpan.FromSeconds(1) + TimeSpan.FromTicks(1));
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendRawAsync(HttpMethod.Post, "/proxy/write")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await fixture.SendRawAsync(HttpMethod.Post, "/proxy/write")).StatusCode);
        Assert.Equal(4, script.AttemptCount);
    }

    private static (Cp6RecordedActivity ServerA, Cp6RecordedActivity ClientA, Cp6RecordedActivity ServerB, Cp6RecordedActivity[] Activities)
        TraceChain(Cp6TelemetryRecorder recorder)
    {
        var serverA = FindServer(recorder, "/proxy/read");
        var clientA = Assert.Single(recorder.GetActivities(), activity =>
            activity.Kind == ActivityKind.Client && activity.ParentSpanId == serverA.SpanId);
        var serverB = Assert.Single(recorder.GetActivities(), activity =>
            activity.Kind == ActivityKind.Server && activity.ParentSpanId == clientA.SpanId);
        return (serverA, clientA, serverB, new[] { serverA, clientA, serverB });
    }

    private static Cp6RecordedActivity FindServer(
        Cp6TelemetryRecorder recorder,
        string route,
        long afterSequence = 0) => Assert.Single(recorder.GetActivities(), activity =>
            activity.Sequence > afterSequence &&
            activity.Kind == ActivityKind.Server &&
            activity.Tags.TryGetValue("http.route", out var value) &&
            string.Equals(value?.ToString(), route, StringComparison.Ordinal));

    private static void AssertResource(IReadOnlyDictionary<string, object> resource, string serviceName)
    {
        Assert.Equal(serviceName, resource["service.name"]);
        Assert.Equal("0.8.0-alpha.1", resource["service.version"]);
        Assert.Equal("test", resource["deployment.environment.name"]);
        Assert.Equal("us-east", resource[Cp6TelemetryConventions.RegionTag]);
    }

    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web);
}
