using System.Text.Json;
using System.Text.Json.Nodes;
using CP6.Platform.Deployment;
using Json.Schema;

namespace CP6.Platform.DeploymentTests;

public sealed class P09SchemaTests
{
    private const string ProfileSchemaId =
        "https://cp6.example/contracts/p09/non-production-runtime-profile.v1.schema.json";
    private const string EvidenceSchemaId =
        "https://cp6.example/contracts/p09/rehearsal-evidence.v1.schema.json";

    private static readonly Lazy<JsonSchema> ProfileSchema = new(
        () => LoadSchema(P09ContractTestData.ProfileSchemaPath));
    private static readonly Lazy<JsonSchema> EvidenceSchema = new(
        () => LoadSchema(P09ContractTestData.EvidenceSchemaPath));

    public static TheoryData<string> InvalidProfileExamples => new()
    {
        "non-production-runtime-profile.crm-topic.invalid.json",
        "non-production-runtime-profile.plaintext-secret.invalid.json",
        "non-production-runtime-profile.production.invalid.json"
    };

    public static TheoryData<string> ProfileParityMutations => new()
    {
        "unknown-root",
        "production-environment",
        "crm-app-id",
        "crm-topic",
        "floating-dapr-image",
        "floating-kafka-image",
        "external-host",
        "fixed-public-port",
        "wrong-partitions",
        "write-on-receiver",
        "schema-version",
        "profile-id",
        "orchestration-image",
        "receiver-app-id",
        "provisioner-principal",
        "unauthorized-app-id",
        "consumer-group",
        "publish-name",
        "publish-direction",
        "publish-scope",
        "publish-username-ref",
        "publish-password-ref",
        "subscribe-name",
        "subscribe-direction",
        "subscribe-scope",
        "subscribe-username-ref",
        "subscribe-password-ref",
        "component-count",
        "event-type",
        "retention",
        "max-message-bytes",
        "app-network",
        "runtime-network",
        "kafka-host-port",
        "host-network",
        "privileged",
        "docker-socket",
        "host-path",
        "kubernetes-namespace",
        "kubernetes-label",
        "default-deny",
        "dns-egress",
        "probe-ingress",
        "kafka-egress",
        "forbidden-kinds-order",
        "forbidden-kinds-content",
        "evidence-schema",
        "required-check-removed",
        "required-check-reordered",
        "required-check-duplicated",
        "nested-missing",
        "nested-unknown",
        "nested-wrong-type",
        "acl-removed",
        "acl-reordered",
        "acl-duplicated",
        "component-scope-widened",
        "root-missing",
        "numeric-schema-version",
        "plaintext-secret-field",
        "invalid-string-empty",
        "invalid-string-non-nfc",
        "invalid-string-carriage-return",
        "invalid-string-nul"
    };

    public static TheoryData<string> EvidenceSchemaMutations => new()
    {
        "missing-check",
        "duplicate-check",
        "reordered-check",
        "failed-check-in-pass",
        "uppercase-profile-hash",
        "short-profile-hash",
        "wrong-profile-id",
        "unknown-root",
        "wrong-runtime-type",
        "non-utc-start",
        "invalid-completed",
        "teardown-exit",
        "teardown-container",
        "teardown-network",
        "teardown-volume",
        "teardown-image",
        "teardown-directory",
        "windows-path",
        "unix-path",
        "password-assignment",
        "bearer-credential",
        "negative-teardown-count",
        "empty-summary",
        "long-summary",
        "multiline-summary",
        "invalid-check-id",
        "too-precise-timestamp",
        "oversized-teardown-count"
    };

    public static TheoryData<string, string> EvidenceRuntimeOnlyCrossValueMutations => new()
    {
        { "completed-before-start", "invalid-time" },
        { "publisher-receiver-span-equality", "trace-span" },
        { "invoker-invoked-span-equality", "trace-span" },
        { "duplicate-failed-check-id-with-distinct-object", "duplicate-check" }
    };

    [Fact]
    public void ValidProfile_PassesDraft202012SchemaAndRuntimeValidator()
    {
        var json = P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json");
        var result = Evaluate(P09ContractTestData.ProfileSchemaPath, json);

        Assert.True(result.IsValid, result.ToString());
        var profile = Cp6P09RuntimeProfile.Parse(json);
        Assert.Equal(Cp6P09RuntimeProfile.ExpectedProfileId, profile.ProfileId);
    }

    [Theory]
    [MemberData(nameof(InvalidProfileExamples))]
    public void InvalidProfileExamples_FailSchemaAndRuntimeValidator(string fileName)
    {
        var json = P09ContractTestData.ReadExample(fileName);

        Assert.False(Evaluate(P09ContractTestData.ProfileSchemaPath, json).IsValid);
        Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));
    }

    [Theory]
    [MemberData(nameof(ProfileParityMutations))]
    public void ProfileSchemaAndRuntime_RejectEveryStructurallyExpressibleTask2Mutation(string mutation)
    {
        var json = BuildProfileMutation(mutation);

        Assert.False(Evaluate(P09ContractTestData.ProfileSchemaPath, json).IsValid);
        Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(json));
    }

    [Fact]
    public void ProfileRuntimeOnlyLexicalRules_AreExplicitlyDocumented()
    {
        var valid = P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json");
        var duplicate = valid.Replace(
            "\"schemaVersion\": \"1\",",
            "\"schemaVersion\": \"1\", \"schemaVersion\": \"1\",",
            StringComparison.Ordinal);
        var root = JsonNode.Parse(valid)!.AsObject();
        root["evidence"]!["schemaId"] = "https://cp6.example/cafe\u0301";

        Assert.Equal(
            "duplicate-property",
            Assert.Throws<Cp6P09ContractException>(() => Cp6P09RuntimeProfile.Parse(duplicate)).CheckId);
        Assert.Equal(
            "invalid-string",
            Assert.Throws<Cp6P09ContractException>(() =>
                Cp6P09RuntimeProfile.Parse(root.ToJsonString())).CheckId);
    }

    [Fact]
    public void ValidEvidenceAndSecretNegativeExample_MatchSchemaOutcomes()
    {
        var valid = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var secret = P09ContractTestData.ReadExample("rehearsal-evidence.secret.invalid.json");

        Assert.True(Evaluate(P09ContractTestData.EvidenceSchemaPath, valid).IsValid);
        Assert.False(Evaluate(P09ContractTestData.EvidenceSchemaPath, secret).IsValid);
    }

    [Theory]
    [MemberData(nameof(EvidenceSchemaMutations))]
    public void EvidenceSchema_RejectsStructurallyExpressibleMutations(string mutation)
    {
        var root = P09ContractTestData.ParseValidEvidence();
        ApplyEvidenceSchemaMutation(root, mutation);

        Assert.False(Evaluate(P09ContractTestData.EvidenceSchemaPath, root.ToJsonString()).IsValid);
    }

    [Fact]
    public void FailedEvidence_AllowsAdditionalDistinctSafeDiagnosticChecksInSchemaAndRuntime()
    {
        var root = P09ContractTestData.ParseValidEvidence();
        root["overall"] = "Failed";
        root["checks"]!.AsArray().Add(new JsonObject
        {
            ["id"] = "additional-diagnostic",
            ["result"] = "Failed",
            ["summary"] = "additional failure recorded"
        });
        var json = Cp6P09Json.Canonicalize(root.ToJsonString());

        Assert.True(Evaluate(P09ContractTestData.EvidenceSchemaPath, json).IsValid);
        var evidence = Cp6P09RehearsalEvidence.Parse(json);
        Assert.Equal(13, evidence.Checks.Count);
        Assert.Equal("Failed", evidence.Overall);
    }

    [Theory]
    [MemberData(nameof(EvidenceRuntimeOnlyCrossValueMutations))]
    public void EvidenceCrossValueRules_AreExplicitlyRuntimeOnly(string mutation, string expectedCheckId)
    {
        var root = P09ContractTestData.ParseValidEvidence();
        switch (mutation)
        {
            case "completed-before-start":
                root["completedUtc"] = "2026-08-29T23:59:59Z";
                break;
            case "publisher-receiver-span-equality":
                root["trace"]!["receiverSpanId"] = root["trace"]!["publisherSpanId"]!.DeepClone();
                break;
            case "invoker-invoked-span-equality":
                root["trace"]!["invokedSpanId"] = root["trace"]!["invokerSpanId"]!.DeepClone();
                break;
            case "duplicate-failed-check-id-with-distinct-object":
                root["overall"] = "Failed";
                root["checks"]![1]!["id"] = root["checks"]![0]!["id"]!.DeepClone();
                root["checks"]![1]!["result"] = "Failed";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        var json = Cp6P09Json.Canonicalize(root.ToJsonString());

        Assert.True(Evaluate(P09ContractTestData.EvidenceSchemaPath, json).IsValid);
        Assert.Equal(
            expectedCheckId,
            Assert.Throws<Cp6P09ContractException>(() => Cp6P09RehearsalEvidence.Parse(json)).CheckId);
    }

    [Fact]
    public void EvidenceProfileHashBinding_IsExplicitlyRuntimeOnly()
    {
        var root = P09ContractTestData.ParseValidEvidence();
        root["profileSha256"] = new string('9', 64);
        var json = Cp6P09Json.Canonicalize(root.ToJsonString());
        var profile = Cp6P09RuntimeProfile.Parse(
            P09ContractTestData.ReadExample("non-production-runtime-profile.valid.json"));

        Assert.True(Evaluate(P09ContractTestData.EvidenceSchemaPath, json).IsValid);
        var evidence = Cp6P09RehearsalEvidence.Parse(json);
        Assert.Equal(
            "profile-mismatch",
            Assert.Throws<Cp6P09ContractException>(() => evidence.ValidateAgainst(profile)).CheckId);
    }

    [Fact]
    public void EvidenceCanonicalByteForm_IsExplicitlyRuntimeOnly()
    {
        var canonical = P09ContractTestData.ReadExample("rehearsal-evidence.valid.json");
        var nonCanonical = canonical.Insert(1, " ");

        Assert.True(Evaluate(P09ContractTestData.EvidenceSchemaPath, nonCanonical).IsValid);
        Assert.Equal(
            "non-canonical-evidence",
            Assert.Throws<Cp6P09ContractException>(() =>
                Cp6P09RehearsalEvidence.Parse(nonCanonical)).CheckId);
    }

    [Fact]
    public void Schemas_AreDraft202012WithFixedIdsAndCloseEveryObjectSchema()
    {
        AssertSchemaMetadata(P09ContractTestData.ProfileSchemaPath, ProfileSchemaId);
        AssertSchemaMetadata(P09ContractTestData.EvidenceSchemaPath, EvidenceSchemaId);
    }

    [Fact]
    public void Schemas_UseOnlyLocalReferencesAndContainNoMachineSpecificPathsOrSecretExamples()
    {
        foreach (var path in new[] { P09ContractTestData.ProfileSchemaPath, P09ContractTestData.EvidenceSchemaPath })
        {
            var text = File.ReadAllText(path);
            var root = JsonNode.Parse(text)!;
            var references = DescendantsAndSelf(root)
                .OfType<JsonObject>()
                .Select(schema => schema["$ref"]?.GetValue<string>())
                .Where(reference => reference is not null)
                .Cast<string>()
                .ToArray();

            Assert.All(references, reference => Assert.StartsWith("#/$defs/", reference, StringComparison.Ordinal));
            Assert.DoesNotContain(P09ContractTestData.RepositoryRoot, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void AssertSchemaMetadata(string path, string expectedId)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

        Assert.Equal("https://json-schema.org/draft/2020-12/schema", root["$schema"]!.GetValue<string>());
        Assert.Equal(expectedId, root["$id"]!.GetValue<string>());

        var objectSchemas = DescendantsAndSelf(root)
            .OfType<JsonObject>()
            .Where(schema => schema["type"]?.GetValue<string>() == "object")
            .ToArray();
        Assert.NotEmpty(objectSchemas);
        Assert.All(
            objectSchemas,
            schema => Assert.False(schema["additionalProperties"]?.GetValue<bool>() ?? true));
    }

    private static EvaluationResults Evaluate(string schemaPath, string json)
    {
        var schema = string.Equals(schemaPath, P09ContractTestData.ProfileSchemaPath, StringComparison.Ordinal)
            ? ProfileSchema.Value
            : EvidenceSchema.Value;
        using var document = JsonDocument.Parse(json);
        return schema.Evaluate(
            document.RootElement,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });
    }

    private static JsonSchema LoadSchema(string path) => JsonSchema.FromText(
        File.ReadAllText(path),
        new BuildOptions { Dialect = Dialect.Draft202012 });

    private static IEnumerable<JsonNode> DescendantsAndSelf(JsonNode node)
    {
        yield return node;
        if (node is JsonObject objectNode)
        {
            foreach (var child in objectNode.Select(property => property.Value).Where(value => value is not null))
            {
                foreach (var descendant in DescendantsAndSelf(child!))
                {
                    yield return descendant;
                }
            }
        }
        else if (node is JsonArray arrayNode)
        {
            foreach (var child in arrayNode.Where(value => value is not null))
            {
                foreach (var descendant in DescendantsAndSelf(child!))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static string BuildProfileMutation(string mutation)
    {
        var root = P09ContractTestData.ParseValidProfile();
        var runtime = root["runtime"]!.AsObject();
        var identities = root["identities"]!.AsObject();
        var components = root["components"]!.AsArray();
        var topic = root["topic"]!.AsObject();
        var acls = root["acls"]!.AsArray();
        var compose = root["compose"]!.AsObject();
        var cluster = root["kubernetes"]!.AsObject();
        var evidence = root["evidence"]!.AsObject();

        switch (mutation)
        {
            case "unknown-root": root["unexpected"] = true; break;
            case "production-environment": root["environmentClass"] = "Production"; break;
            case "crm-app-id": identities["publisherAppId"] = "cp6-crm-publisher"; break;
            case "crm-topic": topic["name"] = "cp6.crm.events.v1"; break;
            case "floating-dapr-image": runtime["daprImage"] = "daprio/daprd:latest"; break;
            case "floating-kafka-image": runtime["kafkaImage"] = "apache/kafka:4"; break;
            case "external-host": compose["bootstrapServers"] = "broker.example.invalid:9092"; break;
            case "fixed-public-port": compose["hostBinding"] = "0.0.0.0:9092"; break;
            case "wrong-partitions": topic["partitions"] = 6; break;
            case "write-on-receiver": acls[3]!["operation"] = "Write"; break;
            case "schema-version": root["schemaVersion"] = "2"; break;
            case "profile-id": root["profileId"] = "cp6-platform-p09-other"; break;
            case "orchestration-image": runtime["kubectlImage"] = "registry.k8s.io/kubectl:latest"; break;
            case "receiver-app-id": identities["receiverAppId"] = "cp6-p09-other-receiver"; break;
            case "provisioner-principal": identities["provisionerPrincipal"] = "cp6-p09-other-provisioner"; break;
            case "unauthorized-app-id": identities["unauthorizedAppId"] = "cp6-p09-other-unauthorized"; break;
            case "consumer-group": identities["consumerGroup"] = "cp6-p09-other-group"; break;
            case "publish-name": components[0]!["name"] = "cp6-p09-other-publish"; break;
            case "publish-direction": components[0]!["direction"] = "Subscribe"; break;
            case "publish-scope": components[0]!["scope"]![0] = "cp6-p09-probe-receiver"; break;
            case "publish-username-ref": components[0]!["usernameSecretRef"] = "other-username"; break;
            case "publish-password-ref": components[0]!["passwordSecretRef"] = "other-password"; break;
            case "subscribe-name": components[1]!["name"] = "cp6-p09-other-subscribe"; break;
            case "subscribe-direction": components[1]!["direction"] = "Publish"; break;
            case "subscribe-scope": components[1]!["scope"]![0] = "cp6-p09-probe-publisher"; break;
            case "subscribe-username-ref": components[1]!["usernameSecretRef"] = "other-username"; break;
            case "subscribe-password-ref": components[1]!["passwordSecretRef"] = "other-password"; break;
            case "component-count": components.RemoveAt(components.Count - 1); break;
            case "event-type": topic["eventType"] = "com.gtx537.platform.other.v1"; break;
            case "retention": topic["retentionMs"] = 7_200_000; break;
            case "max-message-bytes": topic["maxMessageBytes"] = 2_097_152; break;
            case "app-network": compose["appNetwork"] = "other-app"; break;
            case "runtime-network": compose["runtimeNetwork"] = "other-runtime"; break;
            case "kafka-host-port": compose["kafkaHostPort"] = true; break;
            case "host-network": compose["hostNetwork"] = true; break;
            case "privileged": compose["privileged"] = true; break;
            case "docker-socket": compose["dockerSocket"] = true; break;
            case "host-path": compose["hostPath"] = true; break;
            case "kubernetes-namespace": cluster["namespace"] = "other"; break;
            case "kubernetes-label": cluster["nonDeployableLabel"] = "cp6.io/nondeployable=false"; break;
            case "default-deny": cluster["defaultDeny"] = false; break;
            case "dns-egress": cluster["dnsEgress"] = false; break;
            case "probe-ingress": cluster["minimalProbeIngress"] = false; break;
            case "kafka-egress": cluster["minimalKafkaEgress"] = false; break;
            case "forbidden-kinds-order": SwapFirstTwo(cluster["forbiddenKinds"]!.AsArray()); break;
            case "forbidden-kinds-content": cluster["forbiddenKinds"]![0] = "ConfigMap"; break;
            case "evidence-schema": evidence["schemaId"] = "https://cp6.example/contracts/other.json"; break;
            case "required-check-removed": evidence["requiredChecks"]!.AsArray().RemoveAt(0); break;
            case "required-check-reordered": SwapFirstTwo(evidence["requiredChecks"]!.AsArray()); break;
            case "required-check-duplicated": evidence["requiredChecks"]!.AsArray().Add("profile-valid"); break;
            case "nested-missing": runtime.Remove("daprImage"); break;
            case "nested-unknown": runtime["unexpected"] = true; break;
            case "nested-wrong-type": runtime["daprImage"] = 1; break;
            case "acl-removed": acls.RemoveAt(acls.Count - 1); break;
            case "acl-reordered": SwapFirstTwo(acls); break;
            case "acl-duplicated": acls.Add(acls[0]!.DeepClone()); break;
            case "component-scope-widened": components[0]!["scope"]!.AsArray().Add("cp6-p09-unauthorized-probe"); break;
            case "root-missing": root.Remove("evidence"); break;
            case "numeric-schema-version": root["schemaVersion"] = 1; break;
            case "plaintext-secret-field": runtime["password"] = "obvious-fake-value"; break;
            case "invalid-string-empty": evidence["schemaId"] = string.Empty; break;
            case "invalid-string-non-nfc": evidence["schemaId"] = "https://cp6.example/cafe\u0301"; break;
            case "invalid-string-carriage-return": evidence["schemaId"] = "bad\rvalue"; break;
            case "invalid-string-nul": evidence["schemaId"] = "bad\0value"; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        return root.ToJsonString();
    }

    private static void ApplyEvidenceSchemaMutation(JsonObject root, string mutation)
    {
        var checks = root["checks"]!.AsArray();
        var teardown = root["teardown"]!.AsObject();
        switch (mutation)
        {
            case "missing-check": checks.RemoveAt(0); break;
            case "duplicate-check": checks.Add(checks[0]!.DeepClone()); break;
            case "reordered-check": SwapFirstTwo(checks); break;
            case "failed-check-in-pass": checks[0]!["result"] = "Failed"; break;
            case "uppercase-profile-hash": root["profileSha256"] = new string('A', 64); break;
            case "short-profile-hash": root["profileSha256"] = new string('a', 63); break;
            case "wrong-profile-id": root["profileId"] = "cp6-platform-p09-other"; break;
            case "unknown-root": root["unexpected"] = true; break;
            case "wrong-runtime-type": root["runtime"] = "not-an-object"; break;
            case "non-utc-start": root["startedUtc"] = "2026-08-30T00:00:00-04:00"; break;
            case "invalid-completed": root["completedUtc"] = "not-a-time"; break;
            case "teardown-exit": teardown["commandExitCode"] = 1; break;
            case "teardown-container": teardown["containerCount"] = 1; break;
            case "teardown-network": teardown["networkCount"] = 1; break;
            case "teardown-volume": teardown["volumeCount"] = 1; break;
            case "teardown-image": teardown["imageCount"] = 1; break;
            case "teardown-directory": teardown["temporaryDirectoryRemoved"] = false; break;
            case "windows-path": checks[0]!["summary"] = "artifact at C:\\agent\\work"; break;
            case "unix-path": checks[0]!["summary"] = "artifact under /home/runner/work"; break;
            case "password-assignment": checks[0]!["summary"] = "password=obvious-fake-value"; break;
            case "bearer-credential": checks[0]!["summary"] = "Bearer obvious-fake-credential"; break;
            case "negative-teardown-count": teardown["containerCount"] = -1; break;
            case "empty-summary": checks[0]!["summary"] = string.Empty; break;
            case "long-summary": checks[0]!["summary"] = new string('a', 161); break;
            case "multiline-summary": checks[0]!["summary"] = "line one\nline two"; break;
            case "invalid-check-id": checks[0]!["id"] = "invalid check id"; break;
            case "too-precise-timestamp": root["startedUtc"] = "2026-08-30T00:00:00.12345678Z"; break;
            case "oversized-teardown-count": teardown["containerCount"] = 2_147_483_648L; break;
            default: throw new ArgumentOutOfRangeException(nameof(mutation));
        }
    }

    private static void SwapFirstTwo(JsonArray values)
    {
        var first = values[0]!.DeepClone();
        var second = values[1]!.DeepClone();
        values[0] = second;
        values[1] = first;
    }
}

internal static class P09ContractTestData
{
    internal static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ContractRoot = Path.Combine(RepositoryRoot, "contracts", "p09");

    internal static string ProfileSchemaPath =>
        Path.Combine(ContractRoot, "non-production-runtime-profile.v1.schema.json");

    internal static string EvidenceSchemaPath =>
        Path.Combine(ContractRoot, "rehearsal-evidence.v1.schema.json");

    internal static string ReadExample(string fileName) =>
        File.ReadAllText(Path.Combine(ContractRoot, "examples", fileName));

    internal static JsonObject ParseValidProfile() =>
        JsonNode.Parse(ReadExample("non-production-runtime-profile.valid.json"))!.AsObject();

    internal static JsonObject ParseValidEvidence() =>
        JsonNode.Parse(ReadExample("rehearsal-evidence.valid.json"))!.AsObject();

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
