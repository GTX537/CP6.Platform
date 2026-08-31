using System.Text;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09ProjectBoundaryTests
{
    [Fact]
    public void AssemblyName_IsCP6PlatformDeployment()
    {
        Assert.Equal("CP6.Platform.Deployment", typeof(Cp6P09RuntimeProfile).Assembly.GetName().Name);
    }

    [Fact]
    public void ProductionAssembly_DoesNotContainRuntimeProcessOrContainerOperations()
    {
        var assemblyBytes = File.ReadAllBytes(typeof(Cp6P09RuntimeProfile).Assembly.Location);

        foreach (var forbidden in new[]
                 {
                     "System.Diagnostics.Process",
                     "GetEnvironmentVariable",
                     "Docker",
                     "kubectl"
                 })
        {
            Assert.True(
                assemblyBytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(forbidden)) < 0,
                $"Production assembly contains UTF-8 text '{forbidden}'.");
            Assert.True(
                assemblyBytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(forbidden)) < 0,
                $"Production assembly contains UTF-16 text '{forbidden}'.");
        }
    }
}
