using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.DeploymentTests;

public sealed class P09ComposeContractTests
{
    private const string FixtureImage = "cp6-platform-p09-fixture:0.9.0-alpha.1";
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ComposeRoot = Path.Combine(RepositoryRoot, "deploy", "p09", "compose");
    private static readonly string ComposePath = Path.Combine(ComposeRoot, "compose.yaml");
    private static readonly string TemplateRoot = Path.Combine(ComposeRoot, "templates");
    private static readonly string[] ExpectedServices =
    [
        "direct-probe",
        "kafka",
        "kafka-admin",
        "publisher",
        "publisher-dapr",
        "receiver",
        "receiver-dapr",
        "unauthorized-dapr"
    ];

    [Fact]
    public void ComposeText_FreezesTheExactIsolatedTopology()
    {
        var compose = ReadRequired(ComposePath);

        Assert.Equal(ExpectedServices, SectionKeys(compose, "services"));
        Assert.Equal(
            new[] { "publisher-app", "receiver-app", "runtime", "unauthorized-app" },
            SectionKeys(compose, "networks"));
        Assert.Equal(new[] { "kafka-data" }, SectionKeys(compose, "volumes"));

        Assert.Equal(new[] { "runtime" }, ServiceNetworks(compose, "kafka"));
        Assert.Equal(new[] { "publisher-app" }, ServiceNetworks(compose, "publisher"));
        Assert.Equal(new[] { "publisher-app", "runtime" }, ServiceNetworks(compose, "publisher-dapr"));
        Assert.Equal(new[] { "receiver-app" }, ServiceNetworks(compose, "receiver"));
        Assert.Equal(new[] { "receiver-app", "runtime" }, ServiceNetworks(compose, "receiver-dapr"));
        Assert.Equal(new[] { "unauthorized-app" }, ServiceNetworks(compose, "direct-probe"));
        Assert.Equal(new[] { "runtime", "unauthorized-app" }, ServiceNetworks(compose, "unauthorized-dapr"));
        Assert.Equal(new[] { "runtime" }, ServiceNetworks(compose, "kafka-admin"));

        var images = ExpectedServices.ToDictionary(
            service => service,
            service => ServiceScalar(compose, service, "image"),
            StringComparer.Ordinal);
        Assert.Equal("apache/kafka:4.3.1", images["kafka"]);
        Assert.Equal("apache/kafka:4.3.1", images["kafka-admin"]);
        Assert.Equal("daprio/daprd:1.18.2", images["publisher-dapr"]);
        Assert.Equal("daprio/daprd:1.18.2", images["receiver-dapr"]);
        Assert.Equal("daprio/daprd:1.18.2", images["unauthorized-dapr"]);
        Assert.Equal(FixtureImage, images["publisher"]);
        Assert.Equal(FixtureImage, images["receiver"]);
        Assert.Equal(FixtureImage, images["direct-probe"]);
        Assert.Single(Regex.Matches(compose, @"(?m)^\s{4}build:\s*$").Cast<Match>());
        Assert.Contains("context: ../../..", ServiceBlock(compose, "publisher"), StringComparison.Ordinal);
        Assert.Contains(
            "dockerfile: tests/CP6.Platform.P09Fixture/Dockerfile",
            ServiceBlock(compose, "publisher"),
            StringComparison.Ordinal);

        Assert.DoesNotContain("ports:", ServiceBlock(compose, "kafka"), StringComparison.Ordinal);
        Assert.Single(Regex.Matches(compose, @"(?m)^\s{4}ports:\s*$").Cast<Match>());
        Assert.Contains(
            "127.0.0.1:${CP6_P09_HOST_PORT:-0}:8080",
            ServiceBlock(compose, "publisher"),
            StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", ServiceBlock(compose, "receiver"), StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", ServiceBlock(compose, "publisher-dapr"), StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", ServiceBlock(compose, "receiver-dapr"), StringComparison.Ordinal);
        Assert.DoesNotContain("ports:", ServiceBlock(compose, "unauthorized-dapr"), StringComparison.Ordinal);

        Assert.Contains("profiles: [\"negative\"]", ServiceBlock(compose, "direct-probe"), StringComparison.Ordinal);
        Assert.Contains("profiles: [\"negative\"]", ServiceBlock(compose, "unauthorized-dapr"), StringComparison.Ordinal);
        Assert.Contains("profiles: [\"provision\"]", ServiceBlock(compose, "kafka-admin"), StringComparison.Ordinal);
        Assert.DoesNotContain("profiles:", ServiceBlock(compose, "publisher"), StringComparison.Ordinal);
        Assert.DoesNotContain("profiles:", ServiceBlock(compose, "receiver"), StringComparison.Ordinal);

        Assert.Contains("http://publisher-dapr:3500", ServiceBlock(compose, "publisher"), StringComparison.Ordinal);
        Assert.Contains("http://publisher-dapr:50001", ServiceBlock(compose, "publisher"), StringComparison.Ordinal);
        Assert.Contains("http://receiver-dapr:3500", ServiceBlock(compose, "receiver"), StringComparison.Ordinal);
        Assert.Contains("http://receiver-dapr:50001", ServiceBlock(compose, "receiver"), StringComparison.Ordinal);
        Assert.Contains("http://unauthorized-dapr:3500", ServiceBlock(compose, "direct-probe"), StringComparison.Ordinal);
        Assert.Contains("http://unauthorized-dapr:50001", ServiceBlock(compose, "direct-probe"), StringComparison.Ordinal);

        Assert.All(
            Regex.Matches(compose, @"(?m)^\s+source:\s*(?<source>\S+)\s*$").Cast<Match>(),
            match =>
            {
                var source = match.Groups["source"].Value;
                Assert.True(
                    string.Equals(source, "kafka-data", StringComparison.Ordinal) ||
                    source.StartsWith("${CP6_P09_RUNTIME_ROOT:", StringComparison.Ordinal),
                    $"Unexpected mount source: {source}");
            });
        Assert.Equal(
            Regex.Matches(compose, @"(?m)^\s+-\s+type:\s*bind\s*$").Count,
            Regex.Matches(compose, @"(?m)^\s+read_only:\s*true\s*$").Count);
    }

    [Fact]
    public void ComposeText_FailsClosedForKafkaAuthenticationAndHostSafety()
    {
        var compose = ReadRequired(ComposePath);
        var kafka = ServiceBlock(compose, "kafka");

        Assert.Contains("apache/kafka:4.3.1", kafka, StringComparison.Ordinal);
        Assert.Contains("/mnt/shared/config", kafka, StringComparison.Ordinal);
        Assert.Contains("/etc/kafka/secrets", kafka, StringComparison.Ordinal);
        Assert.Contains("/etc/kafka/clients", kafka, StringComparison.Ordinal);
        Assert.Contains(
            "-Djava.security.auth.login.config=/etc/kafka/secrets/kafka-jaas.conf",
            kafka,
            StringComparison.Ordinal);
        Assert.Contains("kafka-metadata-quorum.sh", kafka, StringComparison.Ordinal);
        Assert.Contains("--bootstrap-controller", kafka, StringComparison.Ordinal);
        Assert.Contains("localhost:9093", kafka, StringComparison.Ordinal);
        Assert.Contains("--command-config", kafka, StringComparison.Ordinal);

        foreach (var forbidden in new[]
                 {
                     ":latest",
                     "network_mode:",
                     "privileged:",
                     "container_name:",
                     "external:",
                     "/var/run/docker.sock",
                     "host.docker.internal",
                     "authType: none"
                 })
        {
            Assert.DoesNotContain(forbidden, compose, StringComparison.OrdinalIgnoreCase);
        }

        Assert.DoesNotMatch(
            new Regex(@"(?im)^\s*(?:password|token|connectionString|secretValue)\s*[:=]\s*\S+", RegexOptions.CultureInvariant),
            compose);
        Assert.DoesNotMatch(
            new Regex(@"(?i)(?:[A-Za-z]:\\Users\\|/home/[^/$\s]+|/Users/[^/$\s]+)", RegexOptions.CultureInvariant),
            compose);
    }

    [Fact]
    public void Templates_AreTokenizedSecretFreeAndProfileExact()
    {
        var expectedTemplates = new[]
        {
            "kafka-jaas.conf",
            "kafka-publish.yaml",
            "kafka-server.properties",
            "kafka-subscribe.yaml",
            "secret-store.yaml",
            "subscription.yaml"
        };
        Assert.Equal(
            expectedTemplates,
            Directory.GetFiles(TemplateRoot).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());

        var secretStore = ReadTemplate("secret-store.yaml");
        Assert.Contains("name: cp6-p09-local-secret-store", secretStore, StringComparison.Ordinal);
        Assert.Contains("type: secretstores.local.file", secretStore, StringComparison.Ordinal);
        Assert.Contains("value: \"@@CP6_P09_SECRETS_FILE@@\"", secretStore, StringComparison.Ordinal);

        var publisher = ReadTemplate("kafka-publish.yaml");
        AssertKafkaComponent(
            publisher,
            "cp6-p09-kafka-publish",
            "cp6-p09-probe-publisher",
            "publisher-username",
            "publisher-password");
        Assert.DoesNotContain("consumerGroup", publisher, StringComparison.Ordinal);

        var receiver = ReadTemplate("kafka-subscribe.yaml");
        AssertKafkaComponent(
            receiver,
            "cp6-p09-kafka-subscribe",
            "cp6-p09-probe-receiver",
            "receiver-username",
            "receiver-password");
        Assert.Contains("name: consumerGroup", receiver, StringComparison.Ordinal);
        Assert.Contains("value: \"cp6-p09-probe-receiver-v1\"", receiver, StringComparison.Ordinal);

        var subscription = ReadTemplate("subscription.yaml");
        Assert.Equal(
            "cp6-p09-deployment-probe-subscription",
            NestedMapScalar(subscription, "metadata", "name"));
        Assert.Contains("pubsubname: cp6-p09-kafka-subscribe", subscription, StringComparison.Ordinal);
        Assert.Contains("topic: cp6.platform.deployment-probe.v1", subscription, StringComparison.Ordinal);
        Assert.Contains("default: /events/deployment-probe", subscription, StringComparison.Ordinal);
        Assert.Equal(
            new[] { "cp6-p09-probe-receiver" },
            ListValues(subscription, "scopes"));
        Assert.DoesNotContain("cp6-p09-kafka-publish", subscription, StringComparison.Ordinal);

        var server = ReadTemplate("kafka-server.properties");
        foreach (var required in new[]
                 {
                     "process.roles=broker,controller",
                     "node.id=1",
                     "controller.quorum.voters=1@kafka:9093",
                     "listeners=CLIENT://:9092,CONTROLLER://:9093",
                     "advertised.listeners=CLIENT://kafka:9092",
                     "controller.listener.names=CONTROLLER",
                     "inter.broker.listener.name=CLIENT",
                     "listener.security.protocol.map=CLIENT:SASL_PLAINTEXT,CONTROLLER:SASL_PLAINTEXT",
                     "sasl.enabled.mechanisms=PLAIN",
                     "sasl.mechanism.inter.broker.protocol=PLAIN",
                     "sasl.mechanism.controller.protocol=PLAIN",
                     "authorizer.class.name=org.apache.kafka.metadata.authorizer.StandardAuthorizer",
                     "allow.everyone.if.no.acl.found=false",
                     "super.users=User:@@CP6_P09_PROVISIONER_USERNAME@@",
                     "log.dirs=/var/lib/kafka/data"
                 })
        {
            Assert.Contains(required, server, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("User:ANONYMOUS", server, StringComparison.OrdinalIgnoreCase);
        Assert.Single(Regex.Matches(server, @"(?m)^super\.users=").Cast<Match>());

        var jaas = ReadTemplate("kafka-jaas.conf");
        foreach (var token in new[]
                 {
                     "@@CP6_P09_PROVISIONER_USERNAME@@",
                     "@@CP6_P09_PROVISIONER_PASSWORD@@",
                     "@@CP6_P09_PUBLISHER_USERNAME@@",
                     "@@CP6_P09_PUBLISHER_PASSWORD@@",
                     "@@CP6_P09_RECEIVER_USERNAME@@",
                     "@@CP6_P09_RECEIVER_PASSWORD@@",
                     "@@CP6_P09_UNAUTHORIZED_USERNAME@@",
                     "@@CP6_P09_UNAUTHORIZED_PASSWORD@@"
                 })
        {
            Assert.Contains(token, jaas, StringComparison.Ordinal);
        }
        Assert.All(
            Regex.Matches(jaas, "(?m)^\\s*(?:password|user_[^=]+)=\\\"(?<value>[^\\\"]+)\\\"").Cast<Match>(),
            match => Assert.Matches(
                new Regex(@"^@@CP6_P09_[A-Z_]+@@$", RegexOptions.CultureInvariant),
                match.Groups["value"].Value));

        var allAssets = string.Join(
            '\n',
            new[] { ComposePath }.Concat(Directory.GetFiles(TemplateRoot)).Select(ReadRequired));
        foreach (var forbidden in new[] { "crm", "customer", "organization", "production", "business" })
        {
            Assert.DoesNotContain(forbidden, allAssets, StringComparison.OrdinalIgnoreCase);
        }
        Assert.DoesNotMatch(
            new Regex(@"(?i)(?:[A-Za-z]:\\Users\\|/home/[^/$\s]+|/Users/[^/$\s]+)", RegexOptions.CultureInvariant),
            allAssets);
    }

    [Fact]
    public void DockerComposeConfig_WhenAvailable_PreservesTheStaticSecurityContract()
    {
        if (!DockerComposeIsAvailable())
        {
            return;
        }

        using var document = RenderComposeConfig();
        var root = document.RootElement;
        var services = root.GetProperty("services");
        Assert.Equal(ExpectedServices, PropertyNames(services));
        Assert.Equal(
            new[] { "publisher-app", "receiver-app", "runtime", "unauthorized-app" },
            PropertyNames(root.GetProperty("networks")));
        Assert.Equal(new[] { "kafka-data" }, PropertyNames(root.GetProperty("volumes")));

        AssertJsonNetworks(services, "kafka", "runtime");
        AssertJsonNetworks(services, "publisher", "publisher-app");
        AssertJsonNetworks(services, "publisher-dapr", "publisher-app", "runtime");
        AssertJsonNetworks(services, "receiver", "receiver-app");
        AssertJsonNetworks(services, "receiver-dapr", "receiver-app", "runtime");
        AssertJsonNetworks(services, "direct-probe", "unauthorized-app");
        AssertJsonNetworks(services, "unauthorized-dapr", "runtime", "unauthorized-app");
        AssertJsonNetworks(services, "kafka-admin", "runtime");

        Assert.False(services.GetProperty("kafka").TryGetProperty("ports", out _));
        foreach (var service in ExpectedServices.Where(service => service != "publisher"))
        {
            Assert.False(services.GetProperty(service).TryGetProperty("ports", out _));
        }

        var publisherPorts = services.GetProperty("publisher").GetProperty("ports");
        var port = Assert.Single(publisherPorts.EnumerateArray());
        Assert.Equal("127.0.0.1", port.GetProperty("host_ip").GetString());
        Assert.Equal(8080, port.GetProperty("target").GetInt32());
        Assert.Equal("0", port.GetProperty("published").GetString());

        foreach (var service in services.EnumerateObject())
        {
            Assert.False(service.Value.TryGetProperty("privileged", out var privileged) && privileged.GetBoolean());
            Assert.False(service.Value.TryGetProperty("container_name", out _));
            Assert.False(service.Value.TryGetProperty("network_mode", out var mode) &&
                string.Equals(mode.GetString(), "host", StringComparison.OrdinalIgnoreCase));
            if (!service.Value.TryGetProperty("volumes", out var mounts))
            {
                continue;
            }

            foreach (var mount in mounts.EnumerateArray().Where(value =>
                         string.Equals(value.GetProperty("type").GetString(), "bind", StringComparison.Ordinal)))
            {
                Assert.True(mount.GetProperty("read_only").GetBoolean());
                Assert.StartsWith(
                    Path.GetFullPath(ComposeRuntimeRoot),
                    Path.GetFullPath(mount.GetProperty("source").GetString()!),
                    StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private static string ComposeRuntimeRoot => Path.Combine(Path.GetTempPath(), "cp6-p09-compose-contract-only");

    private static void AssertKafkaComponent(
        string text,
        string component,
        string scope,
        string usernameSecret,
        string passwordSecret)
    {
        Assert.Contains($"name: {component}", text, StringComparison.Ordinal);
        Assert.Contains("type: pubsub.kafka", text, StringComparison.Ordinal);
        Assert.Contains("name: authType", text, StringComparison.Ordinal);
        Assert.Contains("value: \"password\"", text, StringComparison.Ordinal);
        Assert.Contains("name: saslMechanism", text, StringComparison.Ordinal);
        Assert.Contains("value: \"PLAIN\"", text, StringComparison.Ordinal);
        Assert.Contains("secretStore: cp6-p09-local-secret-store", text, StringComparison.Ordinal);
        Assert.Contains($"name: {usernameSecret}", text, StringComparison.Ordinal);
        Assert.Contains($"key: {usernameSecret}", text, StringComparison.Ordinal);
        Assert.Contains($"name: {passwordSecret}", text, StringComparison.Ordinal);
        Assert.Contains($"key: {passwordSecret}", text, StringComparison.Ordinal);
        Assert.Equal(new[] { scope }, ListValues(text, "scopes"));
    }

    private static void AssertJsonNetworks(JsonElement services, string service, params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal).ToArray(),
            PropertyNames(services.GetProperty(service).GetProperty("networks")));

    private static bool DockerComposeIsAvailable()
    {
        try
        {
            var result = RunProcess("docker", "compose", "version");
            return result.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static JsonDocument RenderComposeConfig()
    {
        var result = RunProcess(
            "docker",
            "compose",
            "--file",
            ComposePath,
            "--profile",
            "negative",
            "--profile",
            "provision",
            "config",
            "--format",
            "json");
        Assert.True(result.ExitCode == 0, $"docker compose config failed:{Environment.NewLine}{result.Error}");
        return JsonDocument.Parse(result.Output);
    }

    private static ProcessResult RunProcess(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["CP6_P09_RUNTIME_ROOT"] = ComposeRuntimeRoot;
        startInfo.Environment["CP6_P09_CLUSTER_ID"] = "MkU3OEVBNTcwNTJENDM2Qk";
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start docker compose.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ProcessResult(process.ExitCode, output, error);
    }

    private static string[] SectionKeys(string text, string section)
    {
        var block = TopLevelSection(text, section);
        return Regex.Matches(block, @"(?m)^  (?<key>[a-z][a-z0-9-]*):\s*$")
            .Select(match => match.Groups["key"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ServiceBlock(string text, string service)
    {
        var services = TopLevelSection(text, "services");
        var start = services.IndexOf($"  {service}:{Environment.NewLine}", StringComparison.Ordinal);
        if (start < 0)
        {
            start = services.IndexOf($"  {service}:\n", StringComparison.Ordinal);
        }
        Assert.True(start >= 0, $"Service '{service}' is missing.");
        var next = Regex.Match(services[(start + 1)..], @"(?m)^  [a-z][a-z0-9-]*:\s*$");
        return next.Success ? services.Substring(start, next.Index + 1) : services[start..];
    }

    private static string TopLevelSection(string text, string section)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        var marker = Regex.Match(normalized, $@"(?m)^{Regex.Escape(section)}:\s*\n");
        Assert.True(marker.Success, $"Top-level section '{section}' is missing.");
        var contentStart = marker.Index + marker.Length;
        var next = Regex.Match(normalized[contentStart..], @"(?m)^[a-z][a-z0-9-]*:\s*$");
        return next.Success
            ? normalized.Substring(contentStart, next.Index)
            : normalized[contentStart..];
    }

    private static string[] ServiceNetworks(string compose, string service)
    {
        var block = ServiceBlock(compose, service).Replace("\r\n", "\n", StringComparison.Ordinal);
        var match = Regex.Match(block, @"(?m)^    networks:\s*\n(?<body>(?:^      [^\n]*\n?)+)");
        Assert.True(match.Success, $"Service '{service}' has no networks map.");
        return Regex.Matches(match.Groups["body"].Value, @"(?m)^      (?<network>[a-z][a-z0-9-]*):")
            .Select(value => value.Groups["network"].Value)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ServiceScalar(string compose, string service, string key)
    {
        var match = Regex.Match(
            ServiceBlock(compose, service),
            $@"(?m)^    {Regex.Escape(key)}:\s*(?<value>[^\r\n]+)\s*$");
        Assert.True(match.Success, $"Service '{service}' has no '{key}'.");
        return match.Groups["value"].Value.Trim(' ', '\'', '"');
    }

    private static string[] ListValues(string yaml, string key)
    {
        var normalized = yaml.Replace("\r\n", "\n", StringComparison.Ordinal);
        var match = Regex.Match(
            normalized,
            $@"(?m)^\s*{Regex.Escape(key)}:\s*\n(?<body>(?:\s+-\s+[^\n]+\n?)+)");
        Assert.True(match.Success, $"YAML list '{key}' is missing.");
        return Regex.Matches(match.Groups["body"].Value, @"(?m)^\s+-\s+(?<value>[^\s#]+)")
            .Select(value => value.Groups["value"].Value.Trim(' ', '\'', '"'))
            .ToArray();
    }

    private static string NestedMapScalar(string yaml, string map, string key)
    {
        var normalized = yaml.Replace("\r\n", "\n", StringComparison.Ordinal);
        var mapMatch = Regex.Match(
            normalized,
            $@"(?m)^{Regex.Escape(map)}:\s*\n(?<body>(?:^  [^\n]*\n?)+)");
        Assert.True(mapMatch.Success, $"YAML map '{map}' is missing.");
        var scalarMatch = Regex.Match(
            mapMatch.Groups["body"].Value,
            $@"(?m)^  {Regex.Escape(key)}:\s*(?<value>[^\s#]+)\s*$");
        Assert.True(scalarMatch.Success, $"YAML scalar '{map}.{key}' is missing.");
        return scalarMatch.Groups["value"].Value.Trim('\'', '"');
    }

    private static string ReadTemplate(string name) => ReadRequired(Path.Combine(TemplateRoot, name));

    private static string ReadRequired(string path)
    {
        Assert.True(File.Exists(path), $"Required P09 Compose asset is missing: {Path.GetRelativePath(RepositoryRoot, path)}");
        return File.ReadAllText(path);
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();

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

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
