using CP6.Platform.AspNetCore.ObservabilityV1;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.ObservabilityV1;

namespace CP6.Platform.AspNetCoreTests.ObservabilityV1;

public sealed class IdentityNegativeMatrixTests
{
    [Theory]
    [InlineData("service")]
    [InlineData("version")]
    [InlineData("environment")]
    [InlineData("region")]
    [InlineData("packages")]
    [InlineData("configuration")]
    [InlineData("schema")]
    [InlineData("deployment")]
    [InlineData("process")]
    [InlineData("epoch")]
    [InlineData("start")]
    [InlineData("git")]
    public void WrongExpectedBindingCannotMatchOwnedRunningModules(string change)
    {
        var actual = RuntimeIdentityTests.Binding();
        var changed = new RuntimeIdentityBindingV1(actual.Declared with { Service = change == "service" ? "other" : actual.Declared.Service, Version = change == "version" ? "2.0.0" : actual.Declared.Version, GitSha = change == "git" ? new string('0', 40) : actual.Declared.GitSha },
            change == "environment" ? "prod" : actual.Environment, change == "region" ? "west" : actual.Region,
            change == "packages" ? "packages-other" : actual.PackageSetRef,
            change == "configuration" ? "sha256:" + new string('0', 64) : actual.ConfigurationDigest,
            change == "schema" ? "schema-other" : actual.SchemaSetRef, change == "deployment" ? "deploy-other" : actual.DeploymentRef,
            change == "process" ? "process-other" : actual.ProcessInstance, change == "epoch" ? 2 : actual.Epoch,
            change == "start" ? actual.StartedUtc.AddSeconds(-1) : actual.StartedUtc, actual.Freshness);
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(actual, "owned-loader", [bytes]);
        var expected = new RuntimeManifestV1(changed, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [RuntimeIdentityTests.Pin(bytes)]);
        Assert.Equal(IdentityVerdictV1.Mismatch, new RuntimeIdentityEvaluatorV1(expected, observer).Observe().Verdict);
    }

    [Fact]
    public void SourceRevisionIsReadFromLoadedAssemblyMetadataAndWrongPinCannotMatch()
    {
        var binding = RuntimeIdentityTests.Binding(); var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes]);
        var pin = RuntimeIdentityTests.Pin(bytes);
        var observed = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [pin]), observer).Observe();
        var actualSource = typeof(Cp6ReleaseIdentity).Assembly.GetCustomAttributesData().Single(a => a.AttributeType.Name == "AssemblyInformationalVersionAttribute").ConstructorArguments[0].Value!.ToString()!.Split('+')[1];
        Assert.Equal(actualSource, observed.Modules.Single().SourceRevision);
        Assert.NotEqual(binding.Declared.GitSha, observed.Modules.Single().SourceRevision);
        var wrong = new ModulePinV1(pin.AssemblyName, pin.AssemblyVersion, pin.Digest, new string('0', 40));
        Assert.Equal(IdentityVerdictV1.Mismatch, new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [wrong]), observer).Observe().Verdict);
    }

    [Fact]
    public void LoadedModuleNameVersionDigestAndCompleteInventoryAreComparedIndependently()
    {
        var binding = RuntimeIdentityTests.Binding();
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        var pin = RuntimeIdentityTests.Pin(bytes);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes]);
        foreach (var pins in new[]
        {
            new[] { new ModulePinV1("Other.Assembly", pin.AssemblyVersion, pin.Digest) },
            new[] { new ModulePinV1(pin.AssemblyName, "9.0.0.0", pin.Digest) },
            new[] { pin, new ModulePinV1("Extra.Assembly", "1.0.0.0", pin.Digest) }
        }) Assert.Equal(IdentityVerdictV1.Mismatch, new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", pins), observer).Observe().Verdict);
        using var twoModules = new OwnedModuleObserverV1(binding, "owned-loader", [bytes, File.ReadAllBytes(typeof(OwnedModuleObserverV1).Assembly.Location)]);
        Assert.Equal(IdentityVerdictV1.Mismatch, new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", [pin]), twoModules).Observe().Verdict);
    }

    [Fact]
    public void MalformedDuplicateEmptyAndExceptionPathsAreSafeAndCannotBecomeSelfReportMatch()
    {
        var binding = RuntimeIdentityTests.Binding();
        var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        Assert.Throws<ArgumentException>(() => new OwnedModuleObserverV1(binding, "owned-loader", [bytes, bytes]));
        var error = Assert.Throws<ArgumentException>(() => new OwnedModuleObserverV1(binding, "owned-loader", [new byte[64]]));
        Assert.Equal("Owned module loading failed.", error.Message);
        Assert.Throws<ArgumentException>(() => new RuntimeManifestV1(binding, ObservedArtifactKindV1.OwnedManagedModule, "owned-loader", []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuntimeManifestV1(binding, (ObservedArtifactKindV1)123, "owned-loader", [RuntimeIdentityTests.Pin(bytes)]));
        Assert.Throws<ArgumentException>(() => new ModulePinV1("Module", "01.0", "sha256:" + new string('a', 64)));
        Assert.Throws<ArgumentException>(() => new ModulePinV1("Module", "1.0", "sha256:" + new string('A', 64)));
        Assert.Throws<ArgumentException>(() => new OwnedModuleObserverV1(binding, "file:///private", [bytes]));
        Assert.True(typeof(OwnedModuleObserverV1).IsSealed);
        Assert.Empty(typeof(RuntimeIdentityObservationV1).GetConstructors());
        Assert.Equal(IdentityVerdictV1.Unproven, new RuntimeIdentityEvaluatorV1(null, null).Observe().Verdict);
    }

    [Theory]
    [InlineData(ObservedArtifactKindV1.ProcessImage)]
    [InlineData(ObservedArtifactKindV1.ContainerImage)]
    [InlineData(ObservedArtifactKindV1.PackageSet)]
    public void FileSourceMetadataMvidOrOpaquePackageReferencesNeverProveOtherArtifactDomains(ObservedArtifactKindV1 kind)
    {
        var binding = RuntimeIdentityTests.Binding(); var bytes = File.ReadAllBytes(typeof(Cp6ReleaseIdentity).Assembly.Location);
        using var observer = new OwnedModuleObserverV1(binding, "owned-loader", [bytes]);
        var result = new RuntimeIdentityEvaluatorV1(new RuntimeManifestV1(binding, kind, "owned-loader", [RuntimeIdentityTests.Pin(bytes)]), observer).Observe();
        Assert.Equal(IdentityVerdictV1.Unproven, result.Verdict);
        Assert.Equal(EvidenceStatusV1.Unproven, result.CurrentPackageTrust);
    }
}
