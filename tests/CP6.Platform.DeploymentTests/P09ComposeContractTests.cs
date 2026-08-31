using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CP6.Platform.DeploymentTests;

public sealed class P09ComposeContractTests
{
    private const string RuntimeRootExpression = "${CP6_P09_RUNTIME_ROOT:?CP6_P09_RUNTIME_ROOT must be set}";
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
    public void FixtureServices_AreProjectOwnedBuildsWithoutImageFallback()
    {
        var compose = ReadRequired(ComposePath);
        var build = TopLevelSection(compose, "x-cp6-p09-fixture-build");

        Assert.Contains("context: ../../..", build, StringComparison.Ordinal);
        Assert.Contains(
            "dockerfile: tests/CP6.Platform.P09Fixture/Dockerfile",
            build,
            StringComparison.Ordinal);

        foreach (var service in new[] { "publisher", "receiver", "direct-probe" })
        {
            var block = ServiceBlock(compose, service);
            Assert.Contains("build: *cp6-p09-fixture-build", block, StringComparison.Ordinal);
            Assert.DoesNotContain("image:", block, StringComparison.Ordinal);
            Assert.DoesNotContain("pull_policy:", block, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cp6-platform-p09-fixture:", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeOwnershipContract_FreezesNonRootUsersAndSplitBindRoots()
    {
        var compose = ReadRequired(ComposePath);
        var ownership = TopLevelSection(compose, "x-cp6-p09-runtime-ownership");

        Assert.Equal(
            NormalizeBlock(
                """
                  schema-version: "1"
                  outer-directory-mode: "0700"
                  population-directory-mode: "0733"
                  bind-directory-mode: "0711"
                  file-mode: "0600"
                  population-method: target-uid-stdin
                  population-image: apache/kafka:4.3.1
                  targets:
                    kafka:
                      uid: "1000"
                      gid: "1000"
                      mount-sources:
                        - kafka/config
                        - kafka/secrets
                        - kafka/clients
                    kafka-admin:
                      uid: "1000"
                      gid: "1000"
                      mount-sources:
                        - kafka/clients
                    publisher-dapr:
                      uid: "65532"
                      gid: "65532"
                      mount-sources:
                        - dapr/publisher/components
                        - dapr/publisher/secrets
                    receiver-dapr:
                      uid: "65532"
                      gid: "65532"
                      mount-sources:
                        - dapr/receiver/components
                        - dapr/receiver/secrets
                    unauthorized-dapr:
                      uid: "65532"
                      gid: "65532"
                      mount-sources:
                        - dapr/unauthorized/components
                        - dapr/unauthorized/secrets
                """),
            NormalizeBlock(ownership));

        AssertNoGroupOrOtherReadBits("0700");
        AssertNoGroupOrOtherReadBits("0733");
        AssertNoGroupOrOtherReadBits("0711");
        Assert.Equal(0, Convert.ToInt32("0600", 8) & Convert.ToInt32("0077", 8));

        Assert.Equal("1000:1000", ServiceScalar(compose, "kafka", "user"));
        Assert.Equal("1000:1000", ServiceScalar(compose, "kafka-admin", "user"));
        Assert.Equal("65532:65532", ServiceScalar(compose, "publisher-dapr", "user"));
        Assert.Equal("65532:65532", ServiceScalar(compose, "receiver-dapr", "user"));
        Assert.Equal("65532:65532", ServiceScalar(compose, "unauthorized-dapr", "user"));

        AssertExactComposeMountsAndDependencies(compose);
    }

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
        AssertExactDaprNetworkAttachments(compose);
        AssertExactDaprCommands(compose);

        var images = new[] { "kafka", "kafka-admin", "publisher-dapr", "receiver-dapr", "unauthorized-dapr" }.ToDictionary(
            service => service,
            service => ServiceScalar(compose, service, "image"),
            StringComparer.Ordinal);
        Assert.Equal("apache/kafka:4.3.1", images["kafka"]);
        Assert.Equal("apache/kafka:4.3.1", images["kafka-admin"]);
        Assert.Equal("daprio/daprd:1.18.2", images["publisher-dapr"]);
        Assert.Equal("daprio/daprd:1.18.2", images["receiver-dapr"]);
        Assert.Equal("daprio/daprd:1.18.2", images["unauthorized-dapr"]);
        Assert.Equal(3, Regex.Matches(compose, @"(?m)^\s{4}build:\s*\*cp6-p09-fixture-build\s*$").Count);

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

        AssertExactComposeMountsAndDependencies(compose);
        AssertExactComposeRuntimeFields(compose);
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

        foreach (var forbidden in new[]
                 {
                     ":latest",
                     "network_mode:",
                     "privileged:",
                     "container_name:",
                     "external:",
                     "/var/run/docker.sock",
                     "host.docker.internal",
                     "authType: none",
                     "ipv4_address:",
                     "ipv6_address:",
                     "link_local_ips:",
                     "DAPR_HOST_IP"
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
            "name-resolution.yaml",
            "secret-store.yaml",
            "subscription.yaml"
        };
        Assert.Equal(
            expectedTemplates,
            Directory.GetFiles(TemplateRoot).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());

        var secretStore = ReadTemplate("secret-store.yaml");
        AssertExactSecretStore(secretStore);

        var nameResolution = ReadTemplate("name-resolution.yaml");
        AssertExactNameResolution(nameResolution);

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
        AssertExactSubscription(subscription);

        var server = ReadTemplate("kafka-server.properties");
        AssertKafkaServerProperties(server);

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
    public void KafkaComponentValidator_RejectsExternalBrokerMutation()
    {
        var mutated = ReadTemplate("kafka-publish.yaml")
            .Replace("value: \"kafka:9092\"", "value: \"external.example:9092\"", StringComparison.Ordinal);

        Assert.ThrowsAny<Exception>(() => AssertKafkaComponent(
            mutated,
            "cp6-p09-kafka-publish",
            "cp6-p09-probe-publisher",
            "publisher-username",
            "publisher-password"));
    }

    [Fact]
    public void ContractValidators_RejectCredentialPropertyAndTopologyMutations()
    {
        var publisher = ReadTemplate("kafka-publish.yaml").ReplaceLineEndings("\r\n");
        var swappedSecrets = publisher
            .Replace("publisher-username", "receiver-username", StringComparison.Ordinal)
            .Replace("publisher-password", "receiver-password", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertKafkaComponent(
            swappedSecrets,
            "cp6-p09-kafka-publish",
            "cp6-p09-probe-publisher",
            "publisher-username",
            "publisher-password"));

        var literalCredential = ReplaceFirst(
            publisher.ReplaceLineEndings("\n"),
            "      secretKeyRef:\n        name: publisher-password\n        key: publisher-password",
            "      value: \"literal-password\"");
        Assert.ThrowsAny<Exception>(() => AssertKafkaComponent(
            literalCredential,
            "cp6-p09-kafka-publish",
            "cp6-p09-probe-publisher",
            "publisher-username",
            "publisher-password"));

        var duplicateOverride = ReadTemplate("kafka-server.properties") +
            "allow.everyone.if.no.acl.found=true\n";
        Assert.ThrowsAny<Exception>(() => AssertKafkaServerProperties(duplicateOverride));

        var compose = ReadRequired(ComposePath);
        var swappedMount = compose.Replace(
            $"{RuntimeRootExpression}/dapr/publisher/secrets",
            $"{RuntimeRootExpression}/dapr/receiver/secrets",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactComposeMountsAndDependencies(swappedMount));

        var missingHealthDependency = ReplaceFirst(
            compose,
            "condition: service_healthy",
            "condition: service_started");
        Assert.ThrowsAny<Exception>(() => AssertExactComposeMountsAndDependencies(missingHealthDependency));
    }

    [Fact]
    public void RuntimeFieldValidators_RejectCommentAndDuplicateKeyMutations()
    {
        var compose = ReadRequired(ComposePath);
        var commentedProfile = compose.Replace(
            "    profiles: [\"negative\"]",
            "    # profiles: [\"negative\"]",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactComposeRuntimeFields(commentedProfile));

        var commentedHealthCommand = compose.Replace(
            "        - --bootstrap-controller",
            "        - --bootstrap-broker\n        # - --bootstrap-controller",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactComposeRuntimeFields(commentedHealthCommand));

        var subscription = ReadTemplate("subscription.yaml");
        var commentedSubscription = subscription.Replace(
            "  pubsubname: cp6-p09-kafka-subscribe",
            "  pubsubname: cp6-p09-wrong\n  # pubsubname: cp6-p09-kafka-subscribe",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactSubscription(commentedSubscription));

        var secretStore = ReadTemplate("secret-store.yaml");
        var duplicateSecretStoreType = secretStore.Replace(
            "  type: secretstores.local.file",
            "  type: secretstores.local.file\n  type: secretstores.local.env",
            StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactSecretStore(duplicateSecretStoreType));
    }

    [Fact]
    public void ComposeNetworkValidator_RejectsInterfaceAndGatewayMutations()
    {
        var compose = ReadRequired(ComposePath);

        var wrongRuntimeInterface = ReplaceFirst(
            compose,
            "interface_name: eth0",
            "interface_name: eth9");
        Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(wrongRuntimeInterface));

        var wrongRuntimeGateway = ReplaceFirst(
            compose,
            "gw_priority: 1",
            "gw_priority: 0");
        Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(wrongRuntimeGateway));

        var wrongAppGateway = ReplaceFirst(
            compose,
            "gw_priority: 0",
            "gw_priority: 2");
        Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(wrongAppGateway));

        var wrongRuntimeAlias = ReplaceFirst(
            compose,
            "        aliases:\n          - cp6-p09-probe-publisher",
            "        aliases:\n          - cp6-p09-wrong-publisher");
        Assert.ThrowsAny<Exception>(() => AssertExactDaprNetworkAttachments(wrongRuntimeAlias));

        var wrongInternalPort = ReplaceFirst(compose, "      - \"50002\"", "      - \"50003\"");
        Assert.ThrowsAny<Exception>(() => AssertExactDaprCommands(wrongInternalPort));

        var wrongNameResolution = ReadTemplate("name-resolution.yaml")
            .Replace("{appid}:50002", "{appid}:50003", StringComparison.Ordinal);
        Assert.ThrowsAny<Exception>(() => AssertExactNameResolution(wrongNameResolution));
    }

    [Fact]
    public void RuntimePathContainment_RejectsSiblingPrefixEscape()
    {
        var root = Path.GetFullPath(ComposeRuntimeRoot);
        var sibling = Path.GetFullPath(ComposeRuntimeRoot + "-escape");

        Assert.False(IsPathWithin(root, sibling));
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
        AssertJsonNetworkAttachment(services, "publisher-dapr", "runtime", "eth0", 1, "cp6-p09-probe-publisher");
        AssertJsonNetworkAttachment(services, "publisher-dapr", "publisher-app", "eth1", 0);
        AssertJsonNetworkAttachment(services, "receiver-dapr", "runtime", "eth0", 1, "cp6-p09-probe-receiver");
        AssertJsonNetworkAttachment(services, "receiver-dapr", "receiver-app", "eth1", 0);
        AssertJsonNetworkAttachment(services, "unauthorized-dapr", "runtime", "eth0", 1, "cp6-p09-unauthorized-probe");
        AssertJsonNetworkAttachment(services, "unauthorized-dapr", "unauthorized-app", "eth1", 0);
        AssertJsonDaprCommands(services);
        AssertJsonRuntimeFields(services);

        foreach (var service in new[] { "publisher", "receiver", "direct-probe" })
        {
            var fixture = services.GetProperty(service);
            Assert.False(fixture.TryGetProperty("image", out _));
            Assert.False(fixture.TryGetProperty("pull_policy", out _));
            var build = fixture.GetProperty("build");
            Assert.Equal(Path.GetFullPath(RepositoryRoot), Path.GetFullPath(build.GetProperty("context").GetString()!));
            Assert.Equal(
                "tests/CP6.Platform.P09Fixture/Dockerfile",
                build.GetProperty("dockerfile").GetString());
        }

        Assert.Equal("apache/kafka:4.3.1", services.GetProperty("kafka").GetProperty("image").GetString());
        Assert.Equal("apache/kafka:4.3.1", services.GetProperty("kafka-admin").GetProperty("image").GetString());
        Assert.Equal("1000:1000", services.GetProperty("kafka").GetProperty("user").GetString());
        Assert.Equal("1000:1000", services.GetProperty("kafka-admin").GetProperty("user").GetString());
        foreach (var service in new[] { "publisher-dapr", "receiver-dapr", "unauthorized-dapr" })
        {
            Assert.Equal("daprio/daprd:1.18.2", services.GetProperty(service).GetProperty("image").GetString());
            Assert.Equal("65532:65532", services.GetProperty(service).GetProperty("user").GetString());
        }

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
                Assert.True(
                    IsPathWithin(ComposeRuntimeRoot, mount.GetProperty("source").GetString()!),
                    $"Bind source escapes the runtime root: {mount.GetProperty("source").GetString()}");
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
        var parsed = ParseDaprComponent(text);

        Assert.Equal("dapr.io/v1alpha1", parsed.ApiVersion);
        Assert.Equal("Component", parsed.Kind);
        Assert.Equal(component, parsed.Name);
        Assert.Equal("cp6-p09-local-secret-store", parsed.SecretStore);
        Assert.Equal("pubsub.kafka", parsed.Type);
        Assert.Equal("v1", parsed.Version);
        Assert.Equal(new[] { scope }, parsed.Scopes);

        var expectedValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["authType"] = "password",
            ["brokers"] = "kafka:9092",
            ["clientID"] = scope,
            ["disableTls"] = "true",
            ["maxMessageBytes"] = "1048576",
            ["saslMechanism"] = "PLAIN"
        };
        if (string.Equals(component, "cp6-p09-kafka-subscribe", StringComparison.Ordinal))
        {
            expectedValues["consumerGroup"] = "cp6-p09-probe-receiver-v1";
            expectedValues["initialOffset"] = "oldest";
        }

        var expectedKeys = expectedValues.Keys
            .Concat(new[] { "saslPassword", "saslUsername" })
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expectedKeys, parsed.Metadata.Keys.Order(StringComparer.Ordinal).ToArray());

        foreach (var expected in expectedValues)
        {
            var entry = parsed.Metadata[expected.Key];
            Assert.Equal(expected.Value, entry.Value);
            Assert.Null(entry.SecretName);
            Assert.Null(entry.SecretKey);
        }

        AssertSecretReference(parsed.Metadata["saslUsername"], usernameSecret);
        AssertSecretReference(parsed.Metadata["saslPassword"], passwordSecret);
    }

    private static void AssertExactComposeRuntimeFields(string compose)
    {
        var expectedProfiles = ExpectedServiceProfiles();
        var expectedEnvironments = ExpectedServiceEnvironments();

        foreach (var service in ExpectedServices)
        {
            Assert.Equal(expectedProfiles[service], ParseServiceInlineList(compose, service, "profiles"));
            Assert.Equal(
                expectedEnvironments[service].OrderBy(pair => pair.Key, StringComparer.Ordinal),
                ParseServiceEnvironment(compose, service).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }

        var kafkaHealth = RequiredBlock(NormalizeLines(ServiceBlock(compose, "kafka")), 4, "healthcheck");
        Assert.Equal(
            new[] { "interval", "retries", "start_period", "test", "timeout" },
            DirectMapKeys(kafkaHealth, 6));
        Assert.Equal("5s", RequiredScalar(kafkaHealth, 6, "interval"));
        Assert.Equal("5s", RequiredScalar(kafkaHealth, 6, "timeout"));
        Assert.Equal("24", RequiredScalar(kafkaHealth, 6, "retries"));
        Assert.Equal("10s", RequiredScalar(kafkaHealth, 6, "start_period"));
        Assert.Equal(
            new[]
            {
                "CMD",
                "/opt/kafka/bin/kafka-metadata-quorum.sh",
                "--bootstrap-controller",
                "localhost:9093",
                "--command-config",
                "/etc/kafka/clients/readiness.properties",
                "describe",
                "--status"
            },
            ParseDirectList(RequiredBlock(kafkaHealth, 6, "test"), 8));
        foreach (var service in ExpectedServices.Where(service => service != "kafka"))
        {
            Assert.Empty(OptionalBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "healthcheck"));
        }
    }

    private static IReadOnlyDictionary<string, string[]> ExpectedServiceProfiles() =>
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["direct-probe"] = ["negative"],
            ["kafka"] = [],
            ["kafka-admin"] = ["provision"],
            ["publisher"] = [],
            ["publisher-dapr"] = [],
            ["receiver"] = [],
            ["receiver-dapr"] = [],
            ["unauthorized-dapr"] = ["negative"]
        };

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> ExpectedServiceEnvironments() =>
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["direct-probe"] = Map(
                ("ASPNETCORE_URLS", "http://+:8080"),
                ("DAPR_GRPC_ENDPOINT", "http://unauthorized-dapr:50001"),
                ("DAPR_HTTP_ENDPOINT", "http://unauthorized-dapr:3500")),
            ["kafka"] = Map(
                ("CLUSTER_ID", "${CP6_P09_CLUSTER_ID:?CP6_P09_CLUSTER_ID must be set}"),
                ("KAFKA_OPTS", "-Djava.security.auth.login.config=/etc/kafka/secrets/kafka-jaas.conf")),
            ["kafka-admin"] = EmptyMap(),
            ["publisher"] = Map(
                ("ASPNETCORE_URLS", "http://+:8080"),
                ("DAPR_GRPC_ENDPOINT", "http://publisher-dapr:50001"),
                ("DAPR_HTTP_ENDPOINT", "http://publisher-dapr:3500")),
            ["publisher-dapr"] = EmptyMap(),
            ["receiver"] = Map(
                ("ASPNETCORE_URLS", "http://+:8080"),
                ("DAPR_GRPC_ENDPOINT", "http://receiver-dapr:50001"),
                ("DAPR_HTTP_ENDPOINT", "http://receiver-dapr:3500")),
            ["receiver-dapr"] = EmptyMap(),
            ["unauthorized-dapr"] = EmptyMap()
        };

    private static void AssertExactSecretStore(string yaml)
    {
        var lines = NormalizeLines(yaml);
        Assert.Equal(new[] { "apiVersion", "kind", "metadata", "spec" }, DirectMapKeys(lines, 0));
        Assert.Equal("dapr.io/v1alpha1", RequiredScalar(lines, 0, "apiVersion"));
        Assert.Equal("Component", RequiredScalar(lines, 0, "kind"));

        var metadata = RequiredBlock(lines, 0, "metadata");
        Assert.Equal(new[] { "name" }, DirectMapKeys(metadata, 2));
        Assert.Equal("cp6-p09-local-secret-store", RequiredScalar(metadata, 2, "name"));

        var spec = RequiredBlock(lines, 0, "spec");
        Assert.Equal(new[] { "metadata", "type", "version" }, DirectMapKeys(spec, 2));
        Assert.Equal("secretstores.local.file", RequiredScalar(spec, 2, "type"));
        Assert.Equal("v1", RequiredScalar(spec, 2, "version"));
        var entries = ParseDaprMetadataEntries(RequiredBlock(spec, 2, "metadata"));
        Assert.Equal(
            new[] { "multiValued", "nestedSeparator", "secretsFile" },
            entries.Keys.Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("@@CP6_P09_SECRETS_FILE@@", entries["secretsFile"].Value);
        Assert.Equal(":", entries["nestedSeparator"].Value);
        Assert.Equal("false", entries["multiValued"].Value);
        Assert.All(entries.Values, entry =>
        {
            Assert.Null(entry.SecretName);
            Assert.Null(entry.SecretKey);
        });
    }

    private static void AssertExactNameResolution(string yaml)
    {
        var lines = NormalizeLines(yaml);
        Assert.Equal(new[] { "apiVersion", "kind", "metadata", "spec" }, DirectMapKeys(lines, 0));
        Assert.Equal("dapr.io/v1alpha1", RequiredScalar(lines, 0, "apiVersion"));
        Assert.Equal("Configuration", RequiredScalar(lines, 0, "kind"));

        var metadata = RequiredBlock(lines, 0, "metadata");
        Assert.Equal(new[] { "name" }, DirectMapKeys(metadata, 2));
        Assert.Equal("cp6-p09-docker-dns", RequiredScalar(metadata, 2, "name"));

        var spec = RequiredBlock(lines, 0, "spec");
        Assert.Equal(new[] { "nameResolution" }, DirectMapKeys(spec, 2));
        var nameResolution = RequiredBlock(spec, 2, "nameResolution");
        Assert.Equal(new[] { "component", "configuration" }, DirectMapKeys(nameResolution, 4));
        Assert.Equal("nameformat", RequiredScalar(nameResolution, 4, "component"));
        var configuration = RequiredBlock(nameResolution, 4, "configuration");
        Assert.Equal(new[] { "format" }, DirectMapKeys(configuration, 6));
        Assert.Equal("{appid}:50002", RequiredScalar(configuration, 6, "format"));
    }

    private static void AssertExactSubscription(string yaml)
    {
        var lines = NormalizeLines(yaml);
        Assert.Equal(new[] { "apiVersion", "kind", "metadata", "scopes", "spec" }, DirectMapKeys(lines, 0));
        Assert.Equal("dapr.io/v2alpha1", RequiredScalar(lines, 0, "apiVersion"));
        Assert.Equal("Subscription", RequiredScalar(lines, 0, "kind"));

        var metadata = RequiredBlock(lines, 0, "metadata");
        Assert.Equal(new[] { "name" }, DirectMapKeys(metadata, 2));
        Assert.Equal("cp6-p09-deployment-probe-subscription", RequiredScalar(metadata, 2, "name"));

        var spec = RequiredBlock(lines, 0, "spec");
        Assert.Equal(new[] { "pubsubname", "routes", "topic" }, DirectMapKeys(spec, 2));
        Assert.Equal("cp6-p09-kafka-subscribe", RequiredScalar(spec, 2, "pubsubname"));
        Assert.Equal("cp6.platform.deployment-probe.v1", RequiredScalar(spec, 2, "topic"));
        var routes = RequiredBlock(spec, 2, "routes");
        Assert.Equal(new[] { "default" }, DirectMapKeys(routes, 4));
        Assert.Equal("/events/deployment-probe", RequiredScalar(routes, 4, "default"));
        Assert.Equal(new[] { "cp6-p09-probe-receiver" }, ParseDirectList(RequiredBlock(lines, 0, "scopes"), 2));
    }

    private static void AssertSecretReference(DaprMetadataEntry entry, string expected)
    {
        Assert.Null(entry.Value);
        Assert.Equal(expected, entry.SecretName);
        Assert.Equal(expected, entry.SecretKey);
    }

    private static DaprComponent ParseDaprComponent(string yaml)
    {
        var lines = NormalizeLines(yaml);
        Assert.Equal(
            new[] { "apiVersion", "auth", "kind", "metadata", "scopes", "spec" },
            DirectMapKeys(lines, 0));

        var metadata = RequiredBlock(lines, 0, "metadata");
        Assert.Equal(new[] { "name" }, DirectMapKeys(metadata, 2));
        var auth = RequiredBlock(lines, 0, "auth");
        Assert.Equal(new[] { "secretStore" }, DirectMapKeys(auth, 2));
        var spec = RequiredBlock(lines, 0, "spec");
        Assert.Equal(new[] { "metadata", "type", "version" }, DirectMapKeys(spec, 2));
        var entries = ParseDaprMetadataEntries(RequiredBlock(spec, 2, "metadata"));

        return new DaprComponent(
            RequiredScalar(lines, 0, "apiVersion"),
            RequiredScalar(lines, 0, "kind"),
            RequiredScalar(metadata, 2, "name"),
            RequiredScalar(auth, 2, "secretStore"),
            RequiredScalar(spec, 2, "type"),
            RequiredScalar(spec, 2, "version"),
            entries,
            ParseDirectList(RequiredBlock(lines, 0, "scopes"), 2));
    }

    private static IReadOnlyDictionary<string, DaprMetadataEntry> ParseDaprMetadataEntries(string[] lines)
    {
        var entries = new Dictionary<string, DaprMetadataEntry>(StringComparer.Ordinal);
        for (var index = 0; index < lines.Length;)
        {
            var header = Regex.Match(lines[index], @"^    - name:\s*(?<name>[^\s#]+)\s*$");
            Assert.True(header.Success, $"Unexpected Dapr metadata line: {lines[index]}");
            var name = Unquote(header.Groups["name"].Value);
            Assert.False(entries.ContainsKey(name), $"Duplicate Dapr metadata name '{name}'.");

            var bodyStart = ++index;
            while (index < lines.Length && LeadingSpaces(lines[index]) > 4)
            {
                index++;
            }

            var body = lines[bodyStart..index];
            var valueMatches = body
                .Select(line => Regex.Match(line, @"^      value:\s*(?<value>.+?)\s*$"))
                .Where(match => match.Success)
                .ToArray();
            var secretMarkerCount = body.Count(line => string.Equals(line, "      secretKeyRef:", StringComparison.Ordinal));
            Assert.True(
                (valueMatches.Length == 1 && secretMarkerCount == 0) ||
                (valueMatches.Length == 0 && secretMarkerCount == 1),
                $"Dapr metadata '{name}' must have exactly one value or secretKeyRef.");

            if (valueMatches.Length == 1)
            {
                Assert.Single(body);
                entries.Add(name, new DaprMetadataEntry(Unquote(valueMatches[0].Groups["value"].Value), null, null));
                continue;
            }

            Assert.Equal(3, body.Length);
            Assert.Equal("      secretKeyRef:", body[0]);
            Assert.Equal(new[] { "key", "name" }, DirectMapKeys(body[1..], 8));
            entries.Add(
                name,
                new DaprMetadataEntry(
                    null,
                    RequiredScalar(body[1..], 8, "name"),
                    RequiredScalar(body[1..], 8, "key")));
        }

        return entries;
    }

    private static void AssertJsonNetworks(JsonElement services, string service, params string[] expected) =>
        Assert.Equal(
            expected.Order(StringComparer.Ordinal).ToArray(),
            PropertyNames(services.GetProperty(service).GetProperty("networks")));

    private static void AssertJsonNetworkAttachment(
        JsonElement services,
        string service,
        string network,
        string interfaceName,
        int gatewayPriority,
        params string[] aliases)
    {
        var attachment = services.GetProperty(service).GetProperty("networks").GetProperty(network);
        Assert.Equal(interfaceName, attachment.GetProperty("interface_name").GetString());
        if (aliases.Length == 0)
        {
            Assert.False(attachment.TryGetProperty("aliases", out _));
        }
        else
        {
            Assert.Equal(
                aliases,
                attachment.GetProperty("aliases").EnumerateArray().Select(value => value.GetString()).ToArray());
        }
        if (gatewayPriority == 0)
        {
            Assert.Equal(new[] { "interface_name" }, PropertyNames(attachment));
            Assert.False(attachment.TryGetProperty("gw_priority", out _));
            return;
        }

        Assert.Equal(
            aliases.Length == 0
                ? new[] { "gw_priority", "interface_name" }
                : new[] { "aliases", "gw_priority", "interface_name" },
            PropertyNames(attachment));
        Assert.Equal(gatewayPriority, attachment.GetProperty("gw_priority").GetInt32());
    }

    private static void AssertJsonDaprCommands(JsonElement services)
    {
        AssertDaprCommand(
            services.GetProperty("publisher-dapr").GetProperty("command").EnumerateArray().Select(value => value.GetString()).ToArray(),
            "cp6-p09-probe-publisher",
            "publisher");
        AssertDaprCommand(
            services.GetProperty("receiver-dapr").GetProperty("command").EnumerateArray().Select(value => value.GetString()).ToArray(),
            "cp6-p09-probe-receiver",
            "receiver");
        AssertDaprCommand(
            services.GetProperty("unauthorized-dapr").GetProperty("command").EnumerateArray().Select(value => value.GetString()).ToArray(),
            "cp6-p09-unauthorized-probe",
            "direct-probe");
    }

    private static void AssertJsonRuntimeFields(JsonElement services)
    {
        var expectedProfiles = ExpectedServiceProfiles();
        var expectedEnvironments = ExpectedServiceEnvironments();
        foreach (var service in ExpectedServices)
        {
            var rendered = services.GetProperty(service);
            if (expectedProfiles[service].Length == 0)
            {
                Assert.False(rendered.TryGetProperty("profiles", out _));
            }
            else
            {
                Assert.Equal(
                    expectedProfiles[service],
                    rendered.GetProperty("profiles").EnumerateArray().Select(value => value.GetString()).ToArray());
            }

            var expectedEnvironment = expectedEnvironments[service];
            if (expectedEnvironment.Count == 0)
            {
                Assert.False(rendered.TryGetProperty("environment", out _));
                continue;
            }

            var environment = rendered.GetProperty("environment");
            Assert.Equal(expectedEnvironment.Keys.Order(StringComparer.Ordinal), PropertyNames(environment));
            foreach (var expected in expectedEnvironment)
            {
                var value = environment.GetProperty(expected.Key).GetString();
                Assert.Equal(
                    string.Equals(expected.Key, "CLUSTER_ID", StringComparison.Ordinal)
                        ? "MkU3OEVBNTcwNTJENDM2Qk"
                        : expected.Value,
                    value);
            }
        }

        var health = services.GetProperty("kafka").GetProperty("healthcheck");
        Assert.Equal("5s", health.GetProperty("interval").GetString());
        Assert.Equal("5s", health.GetProperty("timeout").GetString());
        Assert.Equal(24, health.GetProperty("retries").GetInt32());
        Assert.Equal("10s", health.GetProperty("start_period").GetString());
        Assert.Equal(
            new[]
            {
                "CMD",
                "/opt/kafka/bin/kafka-metadata-quorum.sh",
                "--bootstrap-controller",
                "localhost:9093",
                "--command-config",
                "/etc/kafka/clients/readiness.properties",
                "describe",
                "--status"
            },
            health.GetProperty("test").EnumerateArray().Select(value => value.GetString()).ToArray());
        foreach (var service in ExpectedServices.Where(service => service != "kafka"))
        {
            Assert.False(services.GetProperty(service).TryGetProperty("healthcheck", out _));
        }
    }

    private static void AssertKafkaServerProperties(string text)
    {
        var actual = ParseUniqueProperties(text);
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["advertised.listeners"] = "CLIENT://kafka:9092",
            ["allow.everyone.if.no.acl.found"] = "false",
            ["authorizer.class.name"] = "org.apache.kafka.metadata.authorizer.StandardAuthorizer",
            ["auto.create.topics.enable"] = "false",
            ["controller.listener.names"] = "CONTROLLER",
            ["controller.quorum.voters"] = "1@kafka:9093",
            ["early.start.listeners"] = "CONTROLLER",
            ["group.initial.rebalance.delay.ms"] = "0",
            ["inter.broker.listener.name"] = "CLIENT",
            ["listener.security.protocol.map"] = "CLIENT:SASL_PLAINTEXT,CONTROLLER:SASL_PLAINTEXT",
            ["listeners"] = "CLIENT://:9092,CONTROLLER://:9093",
            ["log.dirs"] = "/var/lib/kafka/data",
            ["message.max.bytes"] = "1048576",
            ["node.id"] = "1",
            ["num.partitions"] = "3",
            ["offsets.topic.replication.factor"] = "1",
            ["process.roles"] = "broker,controller",
            ["sasl.enabled.mechanisms"] = "PLAIN",
            ["sasl.mechanism.controller.protocol"] = "PLAIN",
            ["sasl.mechanism.inter.broker.protocol"] = "PLAIN",
            ["share.coordinator.state.topic.min.isr"] = "1",
            ["share.coordinator.state.topic.replication.factor"] = "1",
            ["super.users"] = "User:@@CP6_P09_PROVISIONER_USERNAME@@",
            ["transaction.state.log.min.isr"] = "1",
            ["transaction.state.log.replication.factor"] = "1"
        };

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var property in expected)
        {
            Assert.Equal(property.Value, actual[property.Key]);
        }

        Assert.DoesNotContain("User:ANONYMOUS", actual["super.users"], StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyDictionary<string, string> ParseUniqueProperties(string text)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf('=');
            Assert.True(separator > 0, $"Malformed Kafka property line: {line}");
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            Assert.False(properties.ContainsKey(key), $"Duplicate Kafka property '{key}'.");
            properties.Add(key, value);
        }

        return properties;
    }

    private static void AssertExactComposeMountsAndDependencies(string compose)
    {
        var root = RuntimeRootExpression;
        var expectedMounts = new Dictionary<string, ComposeMount[]>(StringComparer.Ordinal)
        {
            ["direct-probe"] = [],
            ["kafka"] =
            [
                new("bind", $"{root}/kafka/config", "/mnt/shared/config", true),
                new("bind", $"{root}/kafka/secrets", "/etc/kafka/secrets", true),
                new("bind", $"{root}/kafka/clients", "/etc/kafka/clients", true),
                new("volume", "kafka-data", "/var/lib/kafka/data", false)
            ],
            ["kafka-admin"] =
            [
                new("bind", $"{root}/kafka/clients", "/etc/kafka/clients", true)
            ],
            ["publisher"] = [],
            ["publisher-dapr"] =
            [
                new("bind", $"{root}/dapr/publisher/components", "/components", true),
                new("bind", $"{root}/dapr/publisher/secrets", "/run/cp6-p09/secrets", true)
            ],
            ["receiver"] = [],
            ["receiver-dapr"] =
            [
                new("bind", $"{root}/dapr/receiver/components", "/components", true),
                new("bind", $"{root}/dapr/receiver/secrets", "/run/cp6-p09/secrets", true)
            ],
            ["unauthorized-dapr"] =
            [
                new("bind", $"{root}/dapr/unauthorized/components", "/components", true),
                new("bind", $"{root}/dapr/unauthorized/secrets", "/run/cp6-p09/secrets", true)
            ]
        };

        var expectedDependencies = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            ["direct-probe"] = EmptyMap(),
            ["kafka"] = EmptyMap(),
            ["kafka-admin"] = Map(("kafka", "service_healthy")),
            ["publisher"] = EmptyMap(),
            ["publisher-dapr"] = Map(("kafka", "service_healthy"), ("publisher", "service_started")),
            ["receiver"] = EmptyMap(),
            ["receiver-dapr"] = Map(("kafka", "service_healthy"), ("receiver", "service_started")),
            ["unauthorized-dapr"] = Map(("direct-probe", "service_started"), ("kafka", "service_healthy"))
        };

        foreach (var service in ExpectedServices)
        {
            Assert.Equal(expectedMounts[service], ParseServiceMounts(compose, service));
            var actualDependencies = ParseServiceDependencies(compose, service);
            Assert.Equal(
                expectedDependencies[service].OrderBy(pair => pair.Key, StringComparer.Ordinal),
                actualDependencies.OrderBy(pair => pair.Key, StringComparer.Ordinal));
        }
    }

    private static ComposeMount[] ParseServiceMounts(string compose, string service)
    {
        var lines = NormalizeLines(ServiceBlock(compose, service));
        var volumes = OptionalBlock(lines, 4, "volumes");
        if (volumes.Length == 0)
        {
            return [];
        }

        var result = new List<ComposeMount>();
        for (var index = 0; index < volumes.Length;)
        {
            var header = Regex.Match(volumes[index], @"^      - type:\s*(?<type>[^\s#]+)\s*$");
            Assert.True(header.Success, $"Unexpected mount line for service '{service}': {volumes[index]}");
            var type = Unquote(header.Groups["type"].Value);
            var bodyStart = ++index;
            while (index < volumes.Length && LeadingSpaces(volumes[index]) > 6)
            {
                index++;
            }

            var body = volumes[bodyStart..index];
            var expectedKeys = string.Equals(type, "bind", StringComparison.Ordinal)
                ? new[] { "read_only", "source", "target" }
                : new[] { "source", "target" };
            Assert.Equal(expectedKeys, DirectMapKeys(body, 8));
            Assert.Contains(type, new[] { "bind", "volume" });
            var readOnly = string.Equals(type, "bind", StringComparison.Ordinal) &&
                bool.Parse(RequiredScalar(body, 8, "read_only"));
            result.Add(new ComposeMount(
                type,
                RequiredScalar(body, 8, "source"),
                RequiredScalar(body, 8, "target"),
                readOnly));
        }

        return result.ToArray();
    }

    private static IReadOnlyDictionary<string, string> ParseServiceDependencies(string compose, string service)
    {
        var lines = NormalizeLines(ServiceBlock(compose, service));
        var dependencyLines = OptionalBlock(lines, 4, "depends_on");
        var dependencies = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < dependencyLines.Length;)
        {
            var header = Regex.Match(dependencyLines[index], @"^      (?<service>[a-z][a-z0-9-]*):\s*$");
            Assert.True(header.Success, $"Unexpected dependency line for service '{service}': {dependencyLines[index]}");
            var dependency = header.Groups["service"].Value;
            Assert.False(dependencies.ContainsKey(dependency), $"Duplicate dependency '{dependency}' for '{service}'.");
            var bodyStart = ++index;
            while (index < dependencyLines.Length && LeadingSpaces(dependencyLines[index]) > 6)
            {
                index++;
            }

            var body = dependencyLines[bodyStart..index];
            Assert.Equal(new[] { "condition" }, DirectMapKeys(body, 8));
            dependencies.Add(dependency, RequiredScalar(body, 8, "condition"));
        }

        return dependencies;
    }

    private static string[] ParseServiceInlineList(string compose, string service, string key)
    {
        var regex = new Regex(
            $@"^    {Regex.Escape(key)}:\s*\[(?<values>[^\]]*)\]\s*$",
            RegexOptions.CultureInvariant);
        var matches = NormalizeLines(ServiceBlock(compose, service))
            .Select(line => regex.Match(line))
            .Where(match => match.Success)
            .ToArray();
        Assert.True(matches.Length <= 1, $"Service '{service}' has duplicate '{key}' lists.");
        if (matches.Length == 0)
        {
            return [];
        }

        var values = matches[0].Groups["values"].Value;
        if (string.IsNullOrWhiteSpace(values))
        {
            return [];
        }

        var parsed = values.Split(',').Select(Unquote).ToArray();
        Assert.Equal(parsed.Length, parsed.Distinct(StringComparer.Ordinal).Count());
        return parsed;
    }

    private static IReadOnlyDictionary<string, string> ParseServiceEnvironment(string compose, string service)
    {
        var environment = OptionalBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "environment");
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in DirectMapKeys(environment, 6))
        {
            values.Add(key, RequiredScalar(environment, 6, key));
        }

        return values;
    }

    private static IReadOnlyDictionary<string, string> EmptyMap() =>
        new Dictionary<string, string>(StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> Map(params (string Key, string Value)[] values) =>
        values.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

    private static bool DockerComposeIsAvailable()
    {
        try
        {
            var result = RunProcess("docker", "compose", "version");
            Assert.True(
                result.ExitCode == 0,
                $"docker compose is installed but failed:{Environment.NewLine}{result.Error}");
            return true;
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode is 2 or 3)
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
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            try
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5_000);
            }
            catch (InvalidOperationException)
            {
                // The process exited between the timeout check and the kill request.
            }

            throw new TimeoutException($"Process '{fileName}' exceeded the 30 second contract-test timeout.");
        }

        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
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
        var marker = Regex.Match(
            normalized,
            $@"(?m)^{Regex.Escape(section)}:(?:\s+&[a-z][a-z0-9-]*)?\s*\n");
        Assert.True(marker.Success, $"Top-level section '{section}' is missing.");
        var contentStart = marker.Index + marker.Length;
        var next = Regex.Match(normalized[contentStart..], @"(?m)^[a-z][a-z0-9-]*:\s*$");
        return next.Success
            ? normalized.Substring(contentStart, next.Index)
            : normalized[contentStart..];
    }

    private static string[] ServiceNetworks(string compose, string service)
    {
        var networks = RequiredBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "networks");
        return DirectMapKeys(networks, 6);
    }

    private static void AssertExactDaprNetworkAttachments(string compose)
    {
        AssertServiceNetworkAttachment(compose, "publisher-dapr", "runtime", "eth0", "1", "cp6-p09-probe-publisher");
        AssertServiceNetworkAttachment(compose, "publisher-dapr", "publisher-app", "eth1", "0");
        AssertServiceNetworkAttachment(compose, "receiver-dapr", "runtime", "eth0", "1", "cp6-p09-probe-receiver");
        AssertServiceNetworkAttachment(compose, "receiver-dapr", "receiver-app", "eth1", "0");
        AssertServiceNetworkAttachment(compose, "unauthorized-dapr", "runtime", "eth0", "1", "cp6-p09-unauthorized-probe");
        AssertServiceNetworkAttachment(compose, "unauthorized-dapr", "unauthorized-app", "eth1", "0");
    }

    private static void AssertExactDaprCommands(string compose)
    {
        AssertDaprCommand(ParseDirectList(RequiredBlock(NormalizeLines(ServiceBlock(compose, "publisher-dapr")), 4, "command"), 6), "cp6-p09-probe-publisher", "publisher");
        AssertDaprCommand(ParseDirectList(RequiredBlock(NormalizeLines(ServiceBlock(compose, "receiver-dapr")), 4, "command"), 6), "cp6-p09-probe-receiver", "receiver");
        AssertDaprCommand(ParseDirectList(RequiredBlock(NormalizeLines(ServiceBlock(compose, "unauthorized-dapr")), 4, "command"), 6), "cp6-p09-unauthorized-probe", "direct-probe");
    }

    private static void AssertDaprCommand(string?[] command, string appId, string appChannelAddress)
    {
        Assert.Equal(
            new string?[]
            {
                "--app-id", appId,
                "--app-port", "8080",
                "--app-channel-address", appChannelAddress,
                "--dapr-http-port", "3500",
                "--dapr-grpc-port", "50001",
                "--dapr-internal-grpc-port", "50002",
                "--config", "/run/cp6-p09/secrets/name-resolution.yaml",
                "--resources-path", "/components",
                "--log-level", "warn"
            },
            command);
    }

    private static void AssertServiceNetworkAttachment(
        string compose,
        string service,
        string network,
        string interfaceName,
        string gatewayPriority,
        params string[] aliases)
    {
        var networks = RequiredBlock(NormalizeLines(ServiceBlock(compose, service)), 4, "networks");
        var attachment = RequiredBlock(networks, 6, network);
        Assert.Equal(
            aliases.Length == 0
                ? new[] { "gw_priority", "interface_name" }
                : new[] { "aliases", "gw_priority", "interface_name" },
            DirectMapKeys(attachment, 8));
        Assert.Equal(gatewayPriority, RequiredScalar(attachment, 8, "gw_priority"));
        Assert.Equal(interfaceName, RequiredScalar(attachment, 8, "interface_name"));
        if (aliases.Length == 0)
        {
            Assert.Empty(OptionalBlock(attachment, 8, "aliases"));
        }
        else
        {
            Assert.Equal(aliases, ParseDirectList(RequiredBlock(attachment, 8, "aliases"), 10));
        }
    }

    private static string ServiceScalar(string compose, string service, string key)
    {
        var match = Regex.Match(
            ServiceBlock(compose, service),
            $@"(?m)^    {Regex.Escape(key)}:\s*(?<value>[^\r\n]+)\s*$");
        Assert.True(match.Success, $"Service '{service}' has no '{key}'.");
        return match.Groups["value"].Value.Trim(' ', '\'', '"');
    }

    private static string[] NormalizeLines(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Where(line => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#'))
            .ToArray();

    private static string[] DirectMapKeys(string[] lines, int indent)
    {
        var prefix = new string(' ', indent);
        var regex = new Regex(
            $@"^{Regex.Escape(prefix)}(?<key>[A-Za-z][A-Za-z0-9._-]*):(?:\s.*)?$",
            RegexOptions.CultureInvariant);
        var keys = lines
            .Select(line => regex.Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["key"].Value)
            .ToArray();
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        return keys.Order(StringComparer.Ordinal).ToArray();
    }

    private static string[] RequiredBlock(string[] lines, int indent, string key)
    {
        var markers = FindBlockMarkers(lines, indent, key);
        Assert.Single(markers);
        return LinesAfterMarker(lines, markers[0], indent);
    }

    private static string[] OptionalBlock(string[] lines, int indent, string key)
    {
        var markers = FindBlockMarkers(lines, indent, key);
        Assert.True(markers.Length <= 1, $"YAML block '{key}' is duplicated.");
        return markers.Length == 0 ? [] : LinesAfterMarker(lines, markers[0], indent);
    }

    private static int[] FindBlockMarkers(string[] lines, int indent, string key)
    {
        var marker = $"{new string(' ', indent)}{key}:";
        return lines
            .Select((line, index) => (line, index))
            .Where(value => string.Equals(value.line, marker, StringComparison.Ordinal))
            .Select(value => value.index)
            .ToArray();
    }

    private static string[] LinesAfterMarker(string[] lines, int markerIndex, int indent)
    {
        var end = markerIndex + 1;
        while (end < lines.Length && LeadingSpaces(lines[end]) > indent)
        {
            end++;
        }

        return lines[(markerIndex + 1)..end];
    }

    private static string RequiredScalar(string[] lines, int indent, string key)
    {
        var regex = new Regex(
            $@"^{new string(' ', indent)}{Regex.Escape(key)}:\s*(?<value>.+?)\s*$",
            RegexOptions.CultureInvariant);
        var matches = lines.Select(line => regex.Match(line)).Where(match => match.Success).ToArray();
        Assert.Single(matches);
        return Unquote(matches[0].Groups["value"].Value);
    }

    private static string[] ParseDirectList(string[] lines, int indent)
    {
        var regex = new Regex(
            $@"^{new string(' ', indent)}-\s+(?<value>[^\s#]+)\s*$",
            RegexOptions.CultureInvariant);
        var values = new List<string>();
        foreach (var line in lines)
        {
            var match = regex.Match(line);
            Assert.True(match.Success, $"Unexpected YAML list line: {line}");
            values.Add(Unquote(match.Groups["value"].Value));
        }

        Assert.Equal(values.Count, values.Distinct(StringComparer.Ordinal).Count());
        return values.ToArray();
    }

    private static int LeadingSpaces(string value)
    {
        var count = 0;
        while (count < value.Length && value[count] == ' ')
        {
            count++;
        }

        return count;
    }

    private static string Unquote(string value) => value.Trim().Trim('\'', '"');

    private static string NormalizeBlock(string value) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal).Trim('\r', '\n');

    private static string ReplaceFirst(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.Ordinal);
        Assert.True(index >= 0, $"Mutation source '{oldValue}' is missing.");
        return string.Concat(value.AsSpan(0, index), newValue, value.AsSpan(index + oldValue.Length));
    }

    private static bool IsPathWithin(string root, string candidate)
    {
        var separator = Path.DirectorySeparatorChar;
        var normalizedRoot = Path.GetFullPath(root)
            .Replace(Path.AltDirectorySeparatorChar, separator)
            .TrimEnd(separator);
        var normalizedCandidate = Path.GetFullPath(candidate)
            .Replace(Path.AltDirectorySeparatorChar, separator)
            .TrimEnd(separator);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(normalizedRoot, normalizedCandidate, comparison) ||
            normalizedCandidate.StartsWith(normalizedRoot + separator, comparison);
    }

    private static void AssertNoGroupOrOtherReadBits(string mode) =>
        Assert.Equal(0, Convert.ToInt32(mode, 8) & Convert.ToInt32("0044", 8));

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

    private sealed record ComposeMount(string Type, string Source, string Target, bool ReadOnly);

    private sealed record DaprMetadataEntry(string? Value, string? SecretName, string? SecretKey);

    private sealed record DaprComponent(
        string ApiVersion,
        string Kind,
        string Name,
        string SecretStore,
        string Type,
        string Version,
        IReadOnlyDictionary<string, DaprMetadataEntry> Metadata,
        string[] Scopes);

    private sealed record ProcessResult(int ExitCode, string Output, string Error);
}
