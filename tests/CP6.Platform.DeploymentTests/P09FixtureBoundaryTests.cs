using System.Net;
using System.Net.Sockets;
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
        var source = string.Join(
            '\n',
            Directory.GetFiles(FixtureRoot, "*.cs", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.Ordinal)
                .Select(File.ReadAllText));

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

    [Fact]
    public async Task DirectKafkaProbe_ContinuesAfterSocketFailureAndFindsLaterReachableAddress()
    {
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        var attempted = new List<IPAddress>();
        Task ConnectAsync(IPAddress address, CancellationToken _)
        {
            attempted.Add(address);
            return address.Equals(first)
                ? Task.FromException(new SocketException((int)SocketError.HostUnreachable))
                : Task.CompletedTask;
        }

        var reachable = await Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [first, second],
            ConnectAsync,
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        Assert.True(reachable);
        Assert.Equal(new[] { first, second }, attempted);
    }

    [Fact]
    public async Task DirectKafkaProbe_EndpointDoesNotTruncateResolvedAddresses()
    {
        var source = File.ReadAllText(Path.Combine(FixtureRoot, "Program.cs"));
        Assert.DoesNotContain("MaximumDirectKafkaAddresses", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Take(", source, StringComparison.Ordinal);

        var addresses = Enumerable.Range(1, 9)
            .Select(index => IPAddress.Parse($"192.0.2.{index}"))
            .ToArray();
        var attempted = new List<IPAddress>();
        Task ConnectAsync(IPAddress address, CancellationToken _)
        {
            attempted.Add(address);
            return address.Equals(addresses[^1])
                ? Task.CompletedTask
                : Task.FromException(new SocketException((int)SocketError.HostUnreachable));
        }

        var reachable = await Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            addresses,
            ConnectAsync,
            CancellationToken.None,
            TimeSpan.FromSeconds(1));

        Assert.True(reachable);
        Assert.Equal(addresses, attempted);
    }

    [Fact]
    public async Task DirectKafkaProbe_ContinuesAfterLocalTimeoutAndFindsLaterReachableAddress()
    {
        var first = IPAddress.Parse("192.0.2.1");
        var second = IPAddress.Parse("192.0.2.2");
        var attempted = new List<IPAddress>();
        async Task ConnectAsync(IPAddress address, CancellationToken cancellationToken)
        {
            attempted.Add(address);
            if (address.Equals(first))
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
        }

        var reachable = await Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [first, second],
            ConnectAsync,
            CancellationToken.None,
            TimeSpan.FromMilliseconds(20));

        Assert.True(reachable);
        Assert.Equal(new[] { first, second }, attempted);
    }

    [Fact]
    public async Task DirectKafkaProbe_PropagatesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [IPAddress.Loopback],
            (_, _) => Task.CompletedTask,
            cancellation.Token,
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task DirectKafkaProbe_PropagatesCallerCancellationRaisedDuringConnect()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [IPAddress.Loopback],
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            },
            cancellation.Token,
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task DirectKafkaProbe_DoesNotTreatUnrelatedCancellationAsLocalTimeout()
    {
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [IPAddress.Loopback],
            (_, _) => Task.FromCanceled(unrelated.Token),
            CancellationToken.None,
            TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task DirectKafkaProbe_PropagatesCallerCancellationWhenConnectAlsoFails()
    {
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Cp6P09DirectKafkaProbe.CanConnectAnyAsync(
            [IPAddress.Loopback],
            (_, _) =>
            {
                cancellation.Cancel();
                return Task.FromException(new SocketException((int)SocketError.OperationAborted));
            },
            cancellation.Token,
            TimeSpan.FromSeconds(1)));
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
