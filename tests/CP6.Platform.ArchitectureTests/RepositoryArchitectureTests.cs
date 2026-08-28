using System.Xml.Linq;

namespace CP6.Platform.ArchitectureTests;

public sealed class RepositoryArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["CP6.Platform.Contracts"] = [],
            ["CP6.Platform.Abstractions"] = ["CP6.Platform.Contracts"],
            ["CP6.Platform.AspNetCore"] = ["CP6.Platform.Abstractions", "CP6.Platform.Contracts"],
            ["CP6.Platform.Messaging"] = ["CP6.Platform.Contracts"],
            ["CP6.Platform.EntityFramework"] = ["CP6.Platform.Abstractions", "CP6.Platform.Contracts"],
            ["CP6.Platform.Testing"] =
            [
                "CP6.Platform.Abstractions",
                "CP6.Platform.AspNetCore",
                "CP6.Platform.Contracts",
                "CP6.Platform.EntityFramework",
                "CP6.Platform.Messaging"
            ]
        };

    [Fact]
    public void ProductionProjects_ExactlyMatchApprovedPackageSet()
    {
        var actual = LoadProjects().Keys.Order(StringComparer.Ordinal).ToArray();
        var expected = ExpectedDependencies.Keys.Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void ProjectReferences_ExactlyMatchApprovedDependencyDirection()
    {
        foreach (var (packageId, project) in LoadProjects())
        {
            var actual = project.Document.Descendants("ProjectReference")
                .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
                .Order(StringComparer.Ordinal)
                .ToArray();
            var expected = ExpectedDependencies[packageId].Order(StringComparer.Ordinal).ToArray();

            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void Contracts_HasNoExternalOrInternalDependencies()
    {
        var contracts = LoadProjects()["CP6.Platform.Contracts"].Document;

        Assert.Empty(contracts.Descendants("ProjectReference"));
        Assert.Empty(contracts.Descendants("PackageReference"));
        Assert.Empty(contracts.Descendants("FrameworkReference"));
    }

    [Fact]
    public void ProductionProjects_UseOnlyApprovedExternalDependencies_AndOnlyAspNetCoreUsesSharedFramework()
    {
        foreach (var (packageId, project) in LoadProjects())
        {
            var packageReferences = project.Document.Descendants("PackageReference")
                .Select(reference => reference.Attribute("Include")!.Value)
                .ToArray();
            var frameworkReferences = project.Document.Descendants("FrameworkReference")
                .Select(reference => reference.Attribute("Include")!.Value)
                .ToArray();

            if (packageId == "CP6.Platform.AspNetCore")
            {
                Assert.Equal(["Microsoft.AspNetCore.Authentication.JwtBearer"], packageReferences);
                Assert.Equal(["Microsoft.AspNetCore.App"], frameworkReferences);
            }
            else
            {
                Assert.Empty(packageReferences);
                Assert.Empty(frameworkReferences);
            }
        }
    }

    [Fact]
    public void ProjectReferences_StayInsideSourceTree_AndGraphIsAcyclic()
    {
        var sourceRoot = Path.GetFullPath(Path.Combine(RepositoryRoot, "src")) + Path.DirectorySeparatorChar;
        var projects = LoadProjects();

        foreach (var project in projects.Values)
        {
            foreach (var reference in project.Document.Descendants("ProjectReference"))
            {
                var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project.Path)!, reference.Attribute("Include")!.Value));
                Assert.StartsWith(sourceRoot, resolved, StringComparison.OrdinalIgnoreCase);
                Assert.True(File.Exists(resolved), $"Missing project reference: {resolved}");
            }
        }

        foreach (var packageId in projects.Keys)
        {
            Assert.False(HasCycle(packageId, packageId, new HashSet<string>(StringComparer.Ordinal)));
        }
    }

    [Fact]
    public void NuGetConfiguration_MapsPrivatePackagesWithoutCredentials()
    {
        var path = Path.Combine(RepositoryRoot, "NuGet.config");
        var document = XDocument.Load(path);
        var sources = document.Descendants("packageSources").Elements("add")
            .ToDictionary(element => element.Attribute("key")!.Value, element => element.Attribute("value")!.Value);
        var mappings = document.Descendants("packageSource")
            .ToDictionary(
                element => element.Attribute("key")!.Value,
                element => element.Elements("package").Select(pattern => pattern.Attribute("pattern")!.Value).ToArray());

        Assert.Single(document.Descendants("packageSources").Single().Elements("clear"));
        Assert.Equal(["github", "nuget.org"], sources.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(["github", "nuget.org"], mappings.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("https://api.nuget.org/v3/index.json", sources["nuget.org"]);
        Assert.Equal("https://nuget.pkg.github.com/GTX537/index.json", sources["github"]);
        Assert.Equal(["CP6.Platform.*"], mappings["github"]);
        Assert.Equal(["*"], mappings["nuget.org"]);

        var text = File.ReadAllText(path);
        Assert.DoesNotContain("packageSourceCredentials", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", text, StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasCycle(string origin, string current, HashSet<string> path)
    {
        if (!path.Add(current))
        {
            return current == origin;
        }

        var hasCycle = ExpectedDependencies[current].Any(dependency => HasCycle(origin, dependency, new HashSet<string>(path, StringComparer.Ordinal)));
        return hasCycle;
    }

    private static IReadOnlyDictionary<string, ProjectInfo> LoadProjects()
    {
        return Directory.GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => new ProjectInfo(path, XDocument.Load(path)))
            .ToDictionary(
                project => project.Document.Descendants("PackageId").Single().Value,
                project => project,
                StringComparer.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6.Platform repository root.");
    }

    private sealed record ProjectInfo(string Path, XDocument Document);
}
