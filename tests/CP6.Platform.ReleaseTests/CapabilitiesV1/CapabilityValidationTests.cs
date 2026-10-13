using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using CP6.Platform.Release;
using CP6.Platform.Release.CapabilitiesV1;
using Json.Schema;

namespace CP6.Platform.ReleaseTests.CapabilitiesV1;

public sealed class CapabilityValidationTests
{
    [Fact]
    public void Task_local_version_must_bind_full_source_and_unique_attempt()
    {
        foreach (var version in new[] { "0.10.3-wp02.27.g" + new string('b', 40) + ".1",
            "0.10.3-wp02.27.g" + Fixture.Source + ".0", "0.10.3-wp02.27.g" + Fixture.Source + ".01",
            "0.10.3-wp02.27.20261013.1" })
        {
            var f = new Fixture(); f.Root["version"] = version;
            Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
        }
    }

    [Fact]
    public void Hash_and_source_identifiers_do_not_accept_terminal_newline()
    {
        var f = new Fixture(); f.Row["consumerSourceGitSha"] = new string('b', 40) + "\n";
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
        f = new Fixture(); f.Root["packages"]![0]!["sha256"] = new string('c', 64) + "\n";
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
    }

    [Fact]
    public void Conflicting_decisions_for_one_exact_consumer_identity_are_rejected()
    {
        var f = new Fixture(); var conflict = f.Row.DeepClone();
        conflict["compatibilityDecision"] = "Blocked"; conflict["disableReason"] = "Known failure";
        f.Root["compatibility"]!.AsArray().Add(conflict);
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
    }

    [Fact]
    public void Source_and_packaged_schema_must_preserve_original_bytes()
    {
        var f = new Fixture(); f.Row["packagedSchemaSha256"] = new string('c', 64);
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
    }

    [Theory]
    [InlineData("x", true)]
    [InlineData("xy", true)]
    [InlineData("contracts/test.schema.json", true)]
    [InlineData("", false)]
    [InlineData("/", false)]
    [InlineData("/x", false)]
    [InlineData("x/", false)]
    [InlineData("x//y", false)]
    [InlineData(".", false)]
    [InlineData("..", false)]
    [InlineData("./x", false)]
    [InlineData("x/./y", false)]
    [InlineData("x/../y", false)]
    [InlineData("x\\y", false)]
    public void Packaged_schema_path_agrees_with_schema_and_actual_archive(string path, bool valid)
    {
        var f = valid ? new Fixture(packagedSchemaPath: path) : new Fixture();
        f.Row["packagedSchemaPath"] = path;
        var schema = JsonSchema.FromFile(Path.Combine(ReleaseTestData.RepositoryRoot,
            "contracts/release/capabilities/v1/capability-matrix.v1.schema.json"),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (valid)
            Assert.Equal(Cp6CompatibilityDecision.Allowed, f.Evaluate().Compatibility);
        else
            Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
        Assert.Equal(valid, schema.Evaluate(System.Text.Json.JsonSerializer.SerializeToElement(f.Root)).IsValid);
    }

    [Fact]
    public void Lock_matches_its_closed_schema_and_does_not_encode_a_trust_grant()
    {
        var f = new Fixture(); var result = f.Evaluate();
        var schema = JsonSchema.FromFile(Path.Combine(ReleaseTestData.RepositoryRoot,
            "contracts/release/capabilities/v1/consumer-lock.v1.schema.json"));
        using var json = System.Text.Json.JsonDocument.Parse(result.ConsumerLock);
        Assert.True(schema.Evaluate(json.RootElement).IsValid);
        Assert.False(json.RootElement.TryGetProperty("trustCurrentlyAccepted", out _));
    }

    [Theory]
    [InlineData("nuspec")][InlineData("schema")][InlineData("duplicate")]
    public void Updated_manifest_hash_cannot_hide_wrong_archive_contents(string mutation)
    {
        var f = new Fixture(); var id = "CP6.Platform.Contracts";
        using var stream = new MemoryStream(); stream.Write(f.Packages[id].Span);
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Update, true))
        {
            var path = mutation == "nuspec" ? id + ".nuspec" : "contracts/test.schema.json";
            if (mutation != "duplicate") zip.GetEntry(path)!.Delete();
            using var writer = new StreamWriter(zip.CreateEntry(path).Open());
            writer.Write(mutation == "nuspec" ? "<package><metadata><id>wrong</id></metadata></package>" : "wrong schema");
        }
        var bytes = stream.ToArray(); f.Packages[id] = bytes;
        f.Root["packages"]![2]!["sha256"] = Cp6DeterministicJson.Sha256Hex(bytes);
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate());
    }
    [Fact]
    public void Local_bytes_and_shape_do_not_grant_trust_or_adoption()
    {
        var f = new Fixture();
        var result = f.Evaluate();
        Assert.Equal(Cp6CapabilityState.Verified, result.SourceFrozen);
        Assert.Equal(Cp6CapabilityState.Verified, result.ContractShapeValid);
        Assert.Equal(Cp6CapabilityState.Verified, result.ArtifactBytesVerified);
        Assert.Equal(Cp6CapabilityState.Unknown, result.TrustCurrentlyAccepted);
        Assert.Equal("NOT_PUBLISHED", result.PublicationStatus);
        Assert.Equal("NOT_FORMALLY_TRUSTED", result.TrustStatus);
        Assert.Equal(Cp6CompatibilityDecision.Allowed, result.Compatibility);
        Assert.Equal(Cp6CapabilityState.Unknown, result.ConsumerQualified);
        Assert.Equal(Cp6CapabilityState.Unknown, result.HostRegistered);
        Assert.Equal(Cp6CapabilityState.Unknown, result.ConfigEnabled);
        Assert.Equal(Cp6CapabilityState.Unknown, result.RuntimeObserved);
        Assert.Equal(Cp6CapabilityState.Unknown, result.BusinessApplied);
        Assert.Equal(Cp6CapabilityState.Unknown, result.IndependentlyAccepted);
    }

    [Theory]
    [InlineData("sdk")][InlineData("runtimeFramework")][InlineData("os")][InlineData("architecture")]
    [InlineData("consumerApp")][InlineData("host")][InlineData("consumerSourceGitSha")]
    [InlineData("sourceSchemaSha256")][InlineData("packagedSchemaSha256")]
    [InlineData("semanticValidatorIdentity")][InlineData("databaseProvider")]
    [InlineData("databaseDriver")][InlineData("migrationSet")][InlineData("configurationSha256")]
    [InlineData("messageType")][InlineData("registrationPointRef")][InlineData("route")]
    public void Unmeasured_exact_combination_is_unknown(string property)
    {
        var f = new Fixture();
        var request = f.Row.DeepClone().AsObject();
        request[property] = "unmeasured";
        Assert.Equal(Cp6CompatibilityDecision.Unknown, f.Evaluate(request: Fixture.Canonical(request)).Compatibility);
    }

    [Fact]
    public void Unknown_schema_set_is_not_inferred_from_major_version()
    {
        var f = new Fixture();
        var request = f.Row.DeepClone().AsObject();
        request["schemaVersionsAccepted"] = new JsonArray("2.0.0");
        Assert.Equal(Cp6CompatibilityDecision.Unknown, f.Evaluate(request: Fixture.Canonical(request)).Compatibility);
    }

    [Fact]
    public void Approval_is_external_and_matrix_source_evidence_bytes_are_immutable()
    {
        var f = new Fixture();
        var approval = f.Approval;
        f.Root["sourceGitSha"] = new string('c', 40);
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(approval));
        f = new Fixture();
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate(f.Approval with { SourceGitSha = new string('c', 40) }));
        f.Evidence[f.EvidenceHash] = Encoding.UTF8.GetBytes("changed");
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate());
    }

    [Fact]
    public void Actual_archive_bytes_and_exact_package_set_are_required()
    {
        var f = new Fixture();
        f.Packages[Fixture.Ids[0]] = Encoding.UTF8.GetBytes("wrong bytes");
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate());
        f = new Fixture();
        f.Packages.Remove(Fixture.Ids[0]);
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate());
        f = new Fixture();
        f.Packages["CP6.Platform.Unexpected"] = new byte[] { 1 };
        Assert.Throws<Cp6ReleaseContractException>(() => f.Evaluate());
    }

    [Theory]
    [InlineData("version", "latest")][InlineData("version", "0.10.2")]
    [InlineData("sourceGitSha", "main")][InlineData("sourceGitSha", "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    [InlineData("repository", "https://github.com/other/repo")][InlineData("lane", "Unknown")]
    public void Mutable_or_wrong_identity_is_rejected(string field, string value)
    {
        var f = new Fixture(); f.Root[field] = value;
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
    }

    [Fact]
    public void Mixed_packages_hash_aliases_wrong_feed_and_unknown_capabilities_fail_closed()
    {
        Action<Fixture>[] mutations =
        [
            f => f.Root["packages"]![0]!["version"] = "0.10.1",
            f => f.Root["packages"]![0]!["sourceGitSha"] = new string('c', 40),
            f => f.Root["packages"]![0]!["feedIdentity"] = "https://other/feed",
            f => f.Root["packages"]![0]!["sha256"] = f.Root["packages"]![1]!["sha256"]!.GetValue<string>(),
            f => f.Root["packages"]!.AsArray().Add(f.Root["packages"]![0]!.DeepClone()),
            f => f.Root["capabilities"]![0]!["id"] = "unknown",
            f => f.Root["capabilities"]![0]!["version"] = "2.0.0",
            f => f.Root["capabilities"]![0]!["profiles"]!.AsArray().RemoveAt(0),
            f => f.Root["capabilities"]![1]!["profiles"] = new JsonArray("ctx-v2"),
            f => f.Root["compatibility"]![0]!["databaseProvider"] = "SqlServer",
            f => f.Root["compatibility"]![0]!["extra"] = true,
            f => f.Root["trustCurrentlyAccepted"] = true
        ];
        foreach (var mutate in mutations)
        {
            var f = new Fixture(); mutate(f);
            Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(f.Bytes));
        }
    }

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")][InlineData("{\"x\":1,\"\\u0078\":2}")]
    [InlineData("{\"x\":NaN}")][InlineData("{} ")]
    public void Duplicate_malformed_and_noncanonical_bytes_are_rejected(string input) =>
        Assert.Throws<Cp6ReleaseContractException>(() => Cp6CapabilityValidator.ValidateShape(Encoding.UTF8.GetBytes(input)));

    [Fact]
    public void Lock_is_deterministic_binds_exact_bytes_and_is_defensively_owned()
    {
        var f = new Fixture(); var result = f.Evaluate(); var original = result.ConsumerLock;
        Assert.Equal(original, f.Evaluate().ConsumerLock);
        var exposed = result.ConsumerLock; exposed[0] ^= 1;
        Assert.Equal(original, result.ConsumerLock);
        using var document = System.Text.Json.JsonDocument.Parse(original);
        Assert.Equal(f.Approval.ManifestSha256, document.RootElement.GetProperty("matrixSha256").GetString());
        f.Root["compatibility"]![0]!["configurationSha256"] = new string('c', 64);
        Assert.NotEqual(original, f.Evaluate().ConsumerLock);
    }

    [Fact]
    public void Source_schema_and_semantic_shape_agree_for_closed_contract_mutations()
    {
        var schema = JsonSchema.FromFile(Path.Combine(ReleaseTestData.RepositoryRoot,
            "contracts/release/capabilities/v1/capability-matrix.v1.schema.json"));
        Action<Fixture>[] mutations = [ _ => { }, f => f.Root["extra"] = true,
            f => f.Root.Remove("sourceGitSha"), f => f.Root["lane"] = "unknown",
            f => f.Root["capabilities"]![0]!["profiles"] = new JsonArray("unknown"),
            f => f.Root["compatibility"]![0]!["extra"] = true ];
        foreach (var mutate in mutations)
        {
            var f = new Fixture(); mutate(f);
            var valid = Record.Exception(() => Cp6CapabilityValidator.ValidateShape(f.Bytes)) is null;
            Assert.Equal(valid, schema.Evaluate(System.Text.Json.JsonSerializer.SerializeToElement(f.Root)).IsValid);
        }
    }

    internal sealed class Fixture
    {
        internal static readonly string[] Ids = ["CP6.Platform.Abstractions", "CP6.Platform.AspNetCore", "CP6.Platform.Contracts",
            "CP6.Platform.Deployment", "CP6.Platform.EntityFramework", "CP6.Platform.Messaging", "CP6.Platform.Release"];
        internal const string Source = "9259a45d39bae7e98ca804a74778a588c1ad4619";
        internal const string Version = "0.10.3-wp02.27.g" + Source + ".1";
        internal JsonObject Root { get; }
        internal JsonObject Row => Root["compatibility"]![0]!.AsObject();
        internal Dictionary<string, ReadOnlyMemory<byte>> Packages { get; } = new(StringComparer.Ordinal);
        internal Dictionary<string, ReadOnlyMemory<byte>> Evidence { get; } = new(StringComparer.Ordinal);
        internal string EvidenceHash { get; }
        internal byte[] Bytes => Canonical(Root);
        internal Cp6CapabilityApproval Approval => new(Cp6DeterministicJson.Sha256Hex(Bytes), Source);
        internal Fixture(bool formal = false, string packagedSchemaPath = "contracts/test.schema.json")
        {
            var version = formal ? "0.10.2" : Version;
            byte[] evidence = Encoding.UTF8.GetBytes("synthetic unit test evidence; not a real consumer qualification");
            EvidenceHash = Cp6DeterministicJson.Sha256Hex(evidence); Evidence[EvidenceHash] = evidence;
            var packages = new JsonArray();
            foreach (var id in Ids)
            {
                using var stream = new MemoryStream();
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
                        writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><repository type=\"git\" url=\"https://github.com/GTX537/CP6.Platform\" commit=\"{Source}\" /></metadata></package>");
                    using var schema = zip.CreateEntry(packagedSchemaPath).Open(); schema.Write(evidence);
                }
                var bytes = stream.ToArray(); Packages[id] = bytes;
                packages.Add(new JsonObject { ["packageId"] = id, ["version"] = version, ["sourceGitSha"] = Source,
                    ["sha256"] = Cp6DeterministicJson.Sha256Hex(bytes), ["feedIdentity"] = formal ? $"https://nuget.pkg.github.com/GTX537/index.json#{id}/{version}" : $"task-local:wp02-27#{id}/{version}" });
            }
            Root = new JsonObject { ["$schemaId"] = Cp6CapabilityValidator.SchemaId,
                ["repository"] = "https://github.com/GTX537/CP6.Platform", ["sourceGitSha"] = Source,
                ["lane"] = formal ? "FormalPublished" : "TaskLocal", ["version"] = version, ["packages"] = packages,
                ["evidenceSha256"] = new JsonArray(EvidenceHash),
                ["capabilities"] = new JsonArray(
                    new JsonObject { ["id"] = "S01-09", ["version"] = "1.0.0", ["profiles"] = new JsonArray("crm-inq-r3-v15", "erp-credit-current-v1", "erp-credit-frozen-r8"), ["scope"] = new JsonArray("native-contract-routing", "original-json-bytes") },
                    new JsonObject { ["id"] = "ctx-binding-v1", ["version"] = "1.0.0", ["profiles"] = new JsonArray("ctx-v1"), ["scope"] = new JsonArray("Background", "Http", "Message") }),
                ["compatibility"] = new JsonArray(new JsonObject {
                    ["route"] = "unit-test-route", ["consumerApp"] = "unit-test-consumer", ["host"] = "unit-test-host",
                    ["consumerSourceGitSha"] = new string('b', 40), ["producerPackageId"] = "CP6.Platform.Contracts",
                    ["sdk"] = "8.0.424", ["runtimeFramework"] = "Microsoft.NETCore.App/8.0.28", ["os"] = "Windows/10.0.26200", ["architecture"] = "x64",
                    ["sourceSchemaSha256"] = EvidenceHash, ["packagedSchemaSha256"] = EvidenceHash, ["packagedSchemaPath"] = packagedSchemaPath,
                    ["semanticValidatorIdentity"] = "unit-test-validator/1.0.0/sha256:" + EvidenceHash,
                    ["messageType"] = "native-test", ["schemaVersionsAccepted"] = new JsonArray("1.0.0"),
                    ["databaseProvider"] = "PostgreSQL", ["databaseDriver"] = "unit-test-driver/1.0.0", ["migrationSet"] = "sha256:" + EvidenceHash,
                    ["requiredCapabilityId"] = "S01-09", ["requiredCapabilityVersion"] = "1.0.0", ["profile"] = "crm-inq-r3-v15",
                    ["apiSignatureRef"] = "unit-test-api/1", ["registrationPointRef"] = "unit-test-registration/1",
                    ["configurationSha256"] = EvidenceHash, ["compatibilityDecision"] = "Allowed",
                    ["evidenceSha256"] = new JsonArray(EvidenceHash), ["disableReason"] = "" }) };
            if (formal) { Root["capabilities"] = new JsonArray(); Root["compatibility"] = new JsonArray(); }
        }
        internal Cp6CapabilityEvaluation Evaluate(Cp6CapabilityApproval? approval = null, byte[]? request = null) =>
            Cp6CapabilityValidator.Evaluate(Bytes, approval ?? Approval, Packages, Evidence,
                request ?? Canonical(Row), DateTimeOffset.Parse("2026-10-13T00:00:00Z"));
        internal static byte[] Canonical(JsonNode root) => Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(root.ToJsonString()));
    }
}
