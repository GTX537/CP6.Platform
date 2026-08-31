using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CP6.Platform.DeploymentTests;

public sealed class P09FixtureBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string FixtureRoot = Path.Combine(
        RepositoryRoot,
        "tests",
        "CP6.Platform.P09Fixture");

    [Fact]
    public void FixtureProject_HasExactIsolatedDependencies()
    {
        var project = XDocument.Load(Path.Combine(FixtureRoot, "CP6.Platform.P09Fixture.csproj"));

        Assert.Equal("Microsoft.NET.Sdk.Web", project.Root?.Attribute("Sdk")?.Value);
        Assert.Equal("false", project.Descendants("IsPackable").Single().Value);

        var projectReferences = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "CP6.Platform.Contracts",
                "CP6.Platform.Deployment",
                "CP6.Platform.Messaging"
            },
            projectReferences);

        var packageReferences = project.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")!.Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "Dapr.Client" }, packageReferences);
        Assert.All(
            project.Descendants("PackageReference"),
            reference => Assert.Null(reference.Attribute("Version")));
    }

    [Fact]
    public void FixtureSource_ContainsOnlySyntheticP09RuntimeVocabulary()
    {
        var source = File.ReadAllText(Path.Combine(FixtureRoot, "Program.cs"));

        foreach (var role in new[] { "publisher", "receiver", "probe", "unauthorized" })
        {
            Assert.Contains($"\"{role}\"", source, StringComparison.Ordinal);
        }

        foreach (var endpoint in new[]
                 {
                     "/healthz",
                     "/invoke-positive",
                     "/publish-positive",
                     "/dapr/subscribe",
                     "/events/deployment-probe",
                     "/invoked",
                     "/received/{eventId}",
                     "/direct-kafka",
                     "/publish"
                 })
        {
            Assert.Contains($"\"{endpoint}\"", source, StringComparison.Ordinal);
        }

        Assert.Contains("UnknownRoleExitCode = 64", source, StringComparison.Ordinal);
        Assert.Contains("com.gtx537.platform.contract-example.changed.v1", source, StringComparison.Ordinal);
        Assert.Contains("Cp6CloudEventValidator", source, StringComparison.Ordinal);
        Assert.Contains("DaprClient", source, StringComparison.Ordinal);
        Assert.Contains("DaprSidecarPort = 3500", source, StringComparison.Ordinal);
        Assert.Contains("daprEndpointUri.Port != DaprSidecarPort", source, StringComparison.Ordinal);
        Assert.Contains("IsAllowedDaprHost", source, StringComparison.Ordinal);
        Assert.Contains("\"publisher-dapr\"", source, StringComparison.Ordinal);
        Assert.Contains("\"unauthorized-dapr\"", source, StringComparison.Ordinal);

        var forbiddenPatterns = new[]
        {
            @"\bcrm\b",
            @"EntityFramework",
            @"DbContext",
            @"SqlConnection",
            @"NpgsqlConnection",
            @"ConnectionStrings?",
            @"Confluent\.Kafka",
            @"KafkaFlow",
            @"librdkafka",
            @"cp6\.platform\.(?:customer|organization|order|invoice|payment|shipment)"
        };
        Assert.All(
            forbiddenPatterns,
            pattern => Assert.DoesNotMatch(
                new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
                source));
    }

    [Fact]
    public void Dockerfile_IsDeterministicAndNonRoot()
    {
        var dockerfile = File.ReadAllText(Path.Combine(FixtureRoot, "Dockerfile"));

        Assert.Contains("FROM mcr.microsoft.com/dotnet/sdk:8.0.424 AS build", dockerfile, StringComparison.Ordinal);
        Assert.Contains("FROM mcr.microsoft.com/dotnet/aspnet:8.0.30 AS final", dockerfile, StringComparison.Ordinal);
        Assert.Contains("dotnet publish", dockerfile, StringComparison.Ordinal);
        Assert.Contains("--no-restore", dockerfile, StringComparison.Ordinal);
        Assert.Contains("COPY [\"tests/CP6.Platform.P09Fixture/CP6.Platform.P09Fixture.csproj\"", dockerfile, StringComparison.Ordinal);
        Assert.Contains("USER app", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotMatch(
            new Regex(@"(?im)^\s*RUN\s+(?:apt|apt-get|apk|dnf|yum|zypper|pacman|curl|wget|bash|sh|pwsh|powershell)\b", RegexOptions.CultureInvariant),
            dockerfile);
        Assert.DoesNotContain(":latest", dockerfile, StringComparison.OrdinalIgnoreCase);
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
}
