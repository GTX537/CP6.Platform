using CP6.Platform.Contracts.ObservabilityV1;

namespace CP6.Platform.UnitTests.ObservabilityV1;

public sealed class CanonicalBoundaryTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\t")]
    [InlineData("\0")]
    public void CanonicalIdentifiersDigestsModulesSourcesAndTemplatesRejectTrailingControls(string suffix)
    {
        const string digest = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        Assert.Throws<ArgumentException>(() => ObservabilityValueV1.Identifier("orders" + suffix));
        Assert.Throws<ArgumentException>(() => ObservabilityValueV1.Digest(digest + suffix));
        Assert.Throws<ArgumentException>(() => new ModulePinV1("Orders.Module" + suffix, "1.0.0.0", digest));
        Assert.Throws<ArgumentException>(() => new ModulePinV1("Orders.Module", "1.0.0.0", digest, new string('a', 40) + suffix));
        Assert.Throws<ArgumentException>(() => new TelemetryPolicyV1("orders", "test", "east", ["/orders/{id}" + suffix]));
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/orders" + suffix, CapabilityV1.Read, requiredChecks: ["dependency"]));
        Assert.Throws<ArgumentException>(() => new CapabilityRouteV1("/orders", CapabilityV1.NewEffect, "Orders.Module" + suffix, ["dependency"]));
    }
}
