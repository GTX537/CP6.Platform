using System.Security.Cryptography;
using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.ObservabilityV1;
using Xunit.Abstractions;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class RuntimeIdentityTests(ITestOutputHelper output)
{
    [Fact]
    public void OwnedBytesMatchOnlyExactPinnedInventoryAndFreshProcessBinding()
    {
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        var pin = Pin(bytes);
        var binding = Binding();
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes]);
        Array.Fill(bytes, (byte)0);
        var evaluator = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [pin]), observer);
        var result = evaluator.Observe();
        output.WriteLine($"Actual owned loader digest {result.Modules.Single().Digest}; pinned {pin.Digest}; verdict {result.Verdict}; scope {result.Scope}");
        Assert.Equal(IdentityVerdictV1.Match, result.Verdict);
        Assert.Equal("owned-managed-modules-only", result.Scope);
        Assert.Equal(EvidenceStatusV1.Unproven, result.CurrentPackageTrust);
        Assert.DoesNotContain("D:\\", System.Text.Json.JsonSerializer.Serialize(result));
        observer.Dispose();
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    [Fact]
    public void MissingObserverWrongSourceDigestServiceEnvironmentAndKindsDoNotMatch()
    {
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        var binding = Binding();
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes]);
        var good = Pin(bytes);
        foreach (var manifest in new[]
        {
            new RuntimeManifestV1(binding, ObservedArtifactKindV1.ProcessImage, "owned-loader", [good]),
            new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "other-loader", [good]),
            new RuntimeManifestV1(Binding(service: "other"), ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [good]),
            new RuntimeManifestV1(Binding(environment: "prod"), ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [good]),
            new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [new ModulePinV1(good.AssemblyName, good.AssemblyVersion, "sha256:" + new string('0', 64))])
        }) Assert.NotEqual(IdentityVerdictV1.Match, new RuntimeIdentityEvaluatorV1(manifest, observer).Observe().Verdict);
        var valid = new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [good]);
        Assert.Equal(IdentityVerdictV1.Unproven, new RuntimeIdentityEvaluatorV1(valid, null).Observe().Verdict);
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        Assert.Equal(IdentityVerdictV1.Unproven, new RuntimeIdentityEvaluatorV1(valid, observer).Observe(cancelled.Token).Verdict);
        Assert.Throws<ArgumentException>(() => new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [good, good]));
    }

    [Fact]
    public void FreshnessIsEvaluatedOnEveryObservation()
    {
        var clock = new ObservationClock(DateTimeOffset.UtcNow);
        var binding = Binding(start: clock.GetUtcNow().AddSeconds(-1));
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes], clock);
        var evaluator = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [Pin(bytes)]), observer, clock);
        Assert.Equal(IdentityVerdictV1.Match, evaluator.Observe().Verdict);
        clock.Now += TimeSpan.FromMinutes(2);
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    [Fact]
    public void ObserverClockExceptionReturnsSafeUnprovenInsteadOfEscaping()
    {
        var evaluator = new RuntimeIdentityEvaluatorV1(null, null, new ThrowingClock());
        var result = evaluator.Observe();
        Assert.Equal(IdentityVerdictV1.Unproven, result.Verdict);
        Assert.DoesNotContain("SYNTHETIC", System.Text.Json.JsonSerializer.Serialize(result));
    }

    private sealed class ThrowingClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("SYNTHETIC-private-clock");
    }

    [Theory]
    [InlineData("offset")]
    [InlineData("epoch")]
    [InlineData("minimum")]
    public void InvalidObserverTimestampCannotBecomePositiveEvidence(string scenario)
    {
        var now = DateTimeOffset.UtcNow;
        var invalid = scenario switch { "offset" => now.ToOffset(TimeSpan.FromHours(1)), "epoch" => DateTimeOffset.UnixEpoch, _ => DateTimeOffset.MinValue };
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        var error = Assert.Throws<ArgumentException>(() => new OwnedModuleObserverV1(Binding(start: now.AddSeconds(-1)), "owned-loader", [bytes], new ObservationClock(invalid)));
        Assert.Equal("Owned module loading failed.", error.Message);
    }

    [Fact]
    public void FutureObserverTimestampIsUnprovenAtEvaluationTime()
    {
        var now = DateTimeOffset.UtcNow; var binding = Binding(start: now.AddSeconds(-1));
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes], new ObservationClock(now.AddSeconds(1)));
        var evaluator = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [Pin(bytes)]), observer, new ObservationClock(now));
        Assert.Equal(IdentityVerdictV1.Unproven, evaluator.Observe().Verdict);
    }

    internal static ModulePinV1 Pin(byte[] bytes)
    {
        var name = typeof(Cp6ReleaseIdentity).Assembly.GetName();
        return new(name.Name!, name.Version!.ToString(), "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    internal static RuntimeIdentityBindingV1 Binding(string service = "orders", string environment = "test", DateTimeOffset? start = null) => new(
        new Cp6ReleaseIdentity(service, "1.0.0", new string('a', 40), "sha256:" + new string('b', 64), "sha256:" + new string('c', 64), Cp6ReleaseMode.Candidate),
        environment, "east", "packages-a", "sha256:" + new string('d', 64), "schemas-a", "deployment-a", "process-a", 1, start ?? DateTimeOffset.UtcNow.AddSeconds(-1), TimeSpan.FromMinutes(1));
}

internal sealed class ObservationClock(DateTimeOffset now) : TimeProvider
{
    internal DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
