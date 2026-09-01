using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class ReleaseProjectBoundaryTests
{
    [Fact]
    public void Assembly_has_the_expected_public_identity()
    {
        Assert.Equal("CP6.Platform.Release", typeof(Cp6ReleaseContractIds).Assembly.GetName().Name);
    }

    [Fact]
    public void Contract_ids_are_exact_and_unique()
    {
        Assert.Equal(10, Cp6ReleaseContractIds.All.Count);
        Assert.Equal(Cp6ReleaseContractIds.All.Count, Cp6ReleaseContractIds.All.Distinct(StringComparer.Ordinal).Count());
        Assert.All(Cp6ReleaseContractIds.All, id => Assert.StartsWith("https://schemas.cp6.dev/release/", id, StringComparison.Ordinal));
    }
}
