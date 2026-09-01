using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CP6.Platform.Deployment;

namespace CP6.Platform.DeploymentTests;

public sealed class P09ProjectBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly Regex[] ForbiddenSourceOperationPatterns =
    [
        new(
            @"(?:(?:global::)?System\s*\.\s*Diagnostics\s*\.\s*)?Process\s*\.\s*Start\s*\(",
            RegexOptions.CultureInvariant),
        new(
            @"(?:(?:global::)?System\s*\.\s*)?Environment\s*\.\s*GetEnvironmentVariable\s*\(",
            RegexOptions.CultureInvariant)
    ];

    [Fact]
    public void ProjectPackageIdentityAndEvaluatedLocalVersion_AreExact()
    {
        var projectDirectory = Path.Combine(RepositoryRoot, "src", "CP6.Platform.Deployment");
        var project = XDocument.Load(Path.Combine(projectDirectory, "CP6.Platform.Deployment.csproj"));
        var versionPrefix = project.Descendants("VersionPrefix").Single().Value;
        var versionSuffix = project.Descendants("VersionSuffix").Single().Value;

        Assert.Equal("CP6.Platform.Deployment", project.Descendants("PackageId").Single().Value);
        Assert.Equal("0.9.0-alpha.1", $"{versionPrefix}-{versionSuffix}");

        using var assets = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(projectDirectory, "obj", "project.assets.json")));
        var evaluatedProject = assets.RootElement.GetProperty("project");
        Assert.Equal("0.9.0-alpha.1", evaluatedProject.GetProperty("version").GetString());
        Assert.Equal(
            "CP6.Platform.Deployment",
            evaluatedProject.GetProperty("restore").GetProperty("projectName").GetString());
    }

    [Fact]
    public void AssemblyIdentityAndVersions_AreExact()
    {
        var assembly = typeof(Cp6P09RuntimeProfile).Assembly;
        var informationalVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        Assert.Equal("CP6.Platform.Deployment", assembly.GetName().Name);
        Assert.Equal(new Version(0, 9, 0, 0), assembly.GetName().Version);
        Assert.Equal("0.9.0.0", FileVersionInfo.GetVersionInfo(assembly.Location).FileVersion);
        Assert.Matches(
            new Regex(@"\A0\.9\.0-alpha\.1(?:\+[0-9a-f]{40})?\z", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
            informationalVersion);
    }

    [Fact]
    public void ProductionSourcesAndAssembly_DoNotContainRuntimeOperationApis()
    {
        var sourceRoot = Path.Combine(RepositoryRoot, "src", "CP6.Platform.Deployment");
        var sourceText = string.Join(
            '\n',
            Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
                .Where(path => !HasDirectorySegment(path, "bin") && !HasDirectorySegment(path, "obj"))
                .Select(File.ReadAllText));
        var assemblyBytes = File.ReadAllBytes(typeof(Cp6P09RuntimeProfile).Assembly.Location);

        foreach (var forbidden in new[]
                 {
                     "System.Diagnostics.Process",
                     "GetEnvironmentVariable"
                 })
        {
            Assert.False(
                ContainsBytesIgnoringAsciiCase(assemblyBytes, Encoding.UTF8.GetBytes(forbidden)),
                $"Production assembly contains UTF-8 text matching '{forbidden}' without regard to case.");
            Assert.False(
                ContainsBytesIgnoringAsciiCase(assemblyBytes, Encoding.Unicode.GetBytes(forbidden)),
                $"Production assembly contains UTF-16 text matching '{forbidden}' without regard to case.");
        }

        Assert.Empty(FindForbiddenSourceOperations(sourceText));
    }

    [Fact]
    public void SourceOperationScan_RejectsLowercaseDockerProcessInvocationMutation()
    {
        const string mutatedSource = "global::System.Diagnostics.Process.Start(\"docker\", \"run probe\");";

        Assert.NotEmpty(FindForbiddenSourceOperations(mutatedSource));
    }

    [Fact]
    public void ProductionSource_StoresCanonicalRuntimeVocabularyAsLiterals()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "CP6.Platform.Deployment",
            "Cp6P09RuntimeProfileValidator.cs"));

        Assert.Contains("\"kubectlImage\"", source, StringComparison.Ordinal);
        Assert.Contains("\"registry.k8s.io/kubectl:v1.34.1\"", source, StringComparison.Ordinal);
        Assert.Contains("\"kubectl-image\"", source, StringComparison.Ordinal);
        Assert.Contains("\"dockerSocket\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionAssemblyMetadata_DoesNotReferenceProcessOrEnvironmentAccess()
    {
        using var stream = File.OpenRead(typeof(Cp6P09RuntimeProfile).Assembly.Location);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();
        var referencedTypes = metadata.TypeReferences
            .Select(handle => metadata.GetTypeReference(handle))
            .Select(reference => $"{metadata.GetString(reference.Namespace)}.{metadata.GetString(reference.Name)}")
            .ToArray();
        var referencedMembers = metadata.MemberReferences
            .Select(handle => metadata.GetMemberReference(handle))
            .Select(reference => metadata.GetString(reference.Name))
            .ToArray();

        Assert.DoesNotContain("System.Diagnostics.Process", referencedTypes, StringComparer.Ordinal);
        Assert.DoesNotContain("System.Environment", referencedTypes, StringComparer.Ordinal);
        Assert.DoesNotContain("GetEnvironmentVariable", referencedMembers, StringComparer.Ordinal);
    }

    private static bool ContainsBytesIgnoringAsciiCase(ReadOnlySpan<byte> content, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return true;
        }

        for (var offset = 0; offset <= content.Length - value.Length; offset++)
        {
            var matches = true;
            for (var index = 0; index < value.Length; index++)
            {
                if (ToLowerAscii(content[offset + index]) == ToLowerAscii(value[index]))
                {
                    continue;
                }

                matches = false;
                break;
            }

            if (matches)
            {
                return true;
            }
        }

        return false;
    }

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + ('a' - 'A')) : value;

    private static string[] FindForbiddenSourceOperations(string sourceText) =>
        ForbiddenSourceOperationPatterns
            .Where(pattern => pattern.IsMatch(sourceText))
            .Select(pattern => pattern.ToString())
            .ToArray();

    private static bool HasDirectorySegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Contains(segment, StringComparer.OrdinalIgnoreCase);

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
}
