using CP6.Platform.Contracts;
using CP6.Platform.Testing;
namespace CP6.Platform.WP0226.Tests;
public class CapabilityTests
{
    [Theory]
    [InlineData("CP6.Platform.Contracts.SloQualificationV1.ExactNumber", true)]
    [InlineData("CP6.Platform.Contracts.SloQualificationV1.SloQualifier", true)]
    [InlineData("CP6.Platform.Testing.EvidenceV1.EvidenceCase", false)]
    [InlineData("CP6.Platform.Testing.EvidenceV1.DeterministicFaultCase", false)]
    public void Required_opt_in_capabilities_exist(string name, bool contract)
    {
        var assembly = contract ? typeof(Cp6SloEvidenceEvaluator).Assembly : typeof(Cp6TelemetryRecorder).Assembly;
        Assert.NotNull(assembly.GetType(name));
    }
}
