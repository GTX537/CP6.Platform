using System.Net;
using System.Net.Http.Json;
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
using Xunit.Abstractions;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class IdentityClockRegressionTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(120)]
    [InlineData(10)]
    public void ExpiredIdentityCannotReviveWhenUtcRollsBack(int firstElapsedSeconds)
    {
        var clock = new SplitClock(); var initial = clock.UtcNow;
        using var observer = Observer(clock, out var expected);
        var evaluator = new RuntimeIdentityEvaluatorV1(expected, observer, clock);
        Assert.Equal(IdentityVerdictV1.Match, evaluator.Observe().Verdict);
        clock.UtcNow = initial.AddSeconds(120); clock.Elapsed = TimeSpan.FromSeconds(firstElapsedSeconds);
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
        clock.UtcNow = initial.AddSeconds(30); clock.Elapsed += TimeSpan.FromSeconds(10);
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObserverMonotonicExpiryCannotBeHiddenByFrozenOrDifferentEvaluatorClock(bool separateEvaluatorClock)
    {
        var clock = new SplitClock();
        using var observer = Observer(clock, out var expected);
        var evaluationClock = separateEvaluatorClock ? new SplitClock { UtcNow = clock.UtcNow } : clock;
        var evaluator = new RuntimeIdentityEvaluatorV1(expected, observer, evaluationClock);
        Assert.Equal(IdentityVerdictV1.Match, evaluator.Observe().Verdict);
        clock.Elapsed = TimeSpan.FromSeconds(61);
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    [Fact]
    public void FreshEvidenceRemainsMatchUntilTheMonotonicBudgetIsExceeded()
    {
        var clock = new SplitClock();
        using var observer = Observer(clock, out var expected);
        var evaluator = new RuntimeIdentityEvaluatorV1(expected, observer, clock);
        clock.Elapsed = TimeSpan.FromSeconds(59);
        Assert.Equal(IdentityVerdictV1.Match, evaluator.Observe().Verdict);
        clock.Elapsed = TimeSpan.FromSeconds(61);
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    [Fact]
    public void MonotonicClockFailureCannotProducePositiveEvidenceOrExposeException()
    {
        var clock = new SplitClock();
        using var observer = Observer(clock, out var expected);
        var evaluator = new RuntimeIdentityEvaluatorV1(expected, observer, clock);
        clock.ThrowTimestamp = true;
        var result = evaluator.Observe();
        Assert.Equal(IdentityVerdictV1.Unproven, result.Verdict);
        Assert.DoesNotContain("SYNTHETIC", System.Text.Json.JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task ActualHttpIdentityAndEffectRoutesStayClosedAfterExpiryAndUtcRollback()
    {
        var clock = new SplitClock(); var initial = clock.UtcNow;
        using var observer = Observer(clock, out var expected);
        var builder = WebApplication.CreateBuilder(); builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0));
        builder.Services.AddAuthorization(o => o.AddPolicy("operator", p => p.RequireAssertion(_ => true)));
        builder.Services.AddHealthChecks().AddCheck("store", () => HealthCheckResult.Healthy(), tags: ["ready-v1"]);
        builder.Services.AddCp6HealthV1(new HealthProfileV1("operator",
            [new HealthCheckBindingV1("store", "store", HealthPurposeV1.Ready, "ready-v1", DependencyKindV1.Local,
                TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), ProbePolicyV1.ReadOnly, true)],
            [new CapabilityRouteV1("/read", CapabilityV1.Read, requiredChecks: ["store"]),
             new CapabilityRouteV1("/write", CapabilityV1.NewEffect, "CP6.Platform.Contracts", ["store"])]),
            new RuntimeIdentityEvaluatorV1(expected, observer, clock));
        await using var host = builder.Build(); host.MapCp6HealthV1(); await host.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()) };
        using var fresh = await client.GetAsync("/ops/v1/ready");
        Assert.Equal(HttpStatusCode.OK, fresh.StatusCode);
        Assert.True((await fresh.Content.ReadFromJsonAsync<HealthObservationV1>())!.NewEffectsAvailable);
        clock.UtcNow = initial.AddSeconds(120); clock.Elapsed = TimeSpan.FromSeconds(120);
        using var expired = await client.GetAsync("/ops/v1/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, expired.StatusCode);
        clock.UtcNow = initial.AddSeconds(30); clock.Elapsed = TimeSpan.FromSeconds(180);
        using var rollback = await client.GetAsync("/ops/v1/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, rollback.StatusCode);
        var report = (await rollback.Content.ReadFromJsonAsync<HealthObservationV1>())!;
        Assert.True(report.ReadAvailable); Assert.False(report.NewEffectsAvailable);
        var identity = await client.GetFromJsonAsync<System.Text.Json.JsonElement>("/ops/v1/identity");
        Assert.Equal((int)IdentityVerdictV1.Unproven, identity.GetProperty("verdict").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/ops/v1/live")).StatusCode);
        output.WriteLine($"Kestrel {client.BaseAddress}: fresh ready=200, expired ready=503, UTC rollback ready=503; read remains available, effect and identity remain Unproven; real system clock unchanged.");
    }

    private static OwnedModuleObserverV1 Observer(SplitClock clock, out RuntimeManifestV1 expected)
    {
        var binding = RuntimeIdentityTests.Binding(start: clock.UtcNow.AddSeconds(-1));
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        expected = new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [RuntimeIdentityTests.Pin(bytes)]);
        return new OwnedModuleObserverV1(binding, "owned-loader", [bytes], clock);
    }

    private sealed class SplitClock : TimeProvider
    {
        internal DateTimeOffset UtcNow = DateTimeOffset.UtcNow;
        internal TimeSpan Elapsed;
        internal bool ThrowTimestamp;
        public override DateTimeOffset GetUtcNow() => UtcNow;
        public override long GetTimestamp() => ThrowTimestamp ? throw new InvalidOperationException("SYNTHETIC-private-clock") : Elapsed.Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
