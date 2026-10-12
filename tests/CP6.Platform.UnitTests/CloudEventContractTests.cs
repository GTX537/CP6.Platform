using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using CP6.Platform.Messaging;

namespace CP6.Platform.UnitTests;

public sealed class CloudEventContractTests
{
    private static readonly string ContractRoot = FindContractRoot();

    [Fact]
    public void RequiredAttributes_RemainTheOriginalSeven()
    {
        Assert.Equal(
            new[]
            {
                "tenantid", "correlationid", "causationid", "aggregateid", "aggregateversion", "schemaversion", "region"
            },
            Cp6CloudEventAttributes.Required.Select(attribute => attribute.Name));
        Assert.Equal(9, Cp6CloudEventAttributes.All.Count);
        Assert.Equal("traceparent", Cp6CloudEventAttributes.All[7].Name);
        Assert.Equal("tracestate", Cp6CloudEventAttributes.All[8].Name);
    }

    [Fact]
    public void ContractIdentity_MapsTypeToCanonicalSchemaId()
    {
        var identity = Cp6EventContractIdentity.Parse("com.gtx537.crm.opportunity.order-requested.v1");

        Assert.Equal("crm", identity.Producer);
        Assert.Equal("opportunity.order-requested", identity.EventName);
        Assert.Equal("opportunity-order-requested", identity.EventSlug);
        Assert.Equal(1, identity.MajorVersion);
        Assert.Equal(
            "https://contracts.cp6.uk/events/crm/opportunity-order-requested/v1/schema.json",
            identity.SchemaId.AbsoluteUri);
    }

    [Theory]
    [InlineData("com.gtx537.crm.order-created")]
    [InlineData("com.GTX537.crm.order-created.v1")]
    [InlineData("com.gtx537.crm.order_created.v1")]
    [InlineData("com.gtx537.crm.order-created.v0")]
    public void ContractIdentity_RejectsNonCanonicalType(string eventType)
    {
        Assert.Throws<ArgumentException>(() => Cp6EventContractIdentity.Parse(eventType));
    }

    [Fact]
    public void BundleExamples_MatchDeclaredPositiveAndNegativeOutcomes()
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var validator = new Cp6CloudEventValidator(bundle);
        var entry = Assert.Single(bundle.Entries);

        Assert.Equal("1.0", bundle.CloudEventsSpecVersion);
        Assert.Equal(Cp6ContractBundle.Draft202012, bundle.JsonSchemaDialect);
        Assert.Equal(5, entry.Examples.Count);

        foreach (var example in entry.Examples)
        {
            var result = validator.Validate(File.ReadAllBytes(bundle.GetAssetPath(example.Path)));
            Assert.True(
                result.IsValid == example.Valid,
                $"Example '{example.Name}' expected valid={example.Valid} but returned {result.Failure} at {string.Join(',', result.InstanceLocations)}.");
        }
    }

    [Fact]
    public void Codec_CreatesStructuredEventAcceptedByTheBundle()
    {
        var tenantId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        using var dataDocument = JsonDocument.Parse("""
            {
              "resourceId": "22222222-2222-4222-8222-222222222222",
              "version": 8,
              "displayCode": "EX-002"
            }
            """);
        var identity = Cp6EventContractIdentity.Parse("com.gtx537.platform.contract-example.changed.v1");
        var descriptor = new Cp6CloudEventDescriptor(
            "evt-0002",
            new Uri("urn:cp6:platform"),
            identity.EventType,
            $"tenants/{tenantId:D}/contract-examples/example-2",
            new DateTimeOffset(2026, 8, 28, 13, 0, 0, TimeSpan.Zero),
            identity.SchemaId,
            tenantId,
            "corr-0002",
            "cmd-0002",
            "example-2",
            8,
            "1.0.0",
            "na");

        var cloudEvent = Cp6CloudEventCodec.Create(descriptor, dataDocument.RootElement);
        var encoded = Cp6CloudEventCodec.EncodeStructured(cloudEvent);
        var result = new Cp6CloudEventValidator(Cp6ContractBundle.Load(ContractRoot)).Validate(encoded);

        Assert.True(result.IsValid);
        Assert.NotNull(result.CloudEvent);
        Assert.Equal("evt-0002", result.CloudEvent.Id);
        Assert.Equal(8, result.CloudEvent[Cp6CloudEventAttributes.AggregateVersion]);
    }

    [Fact]
    public void ValidationFailure_DoesNotEchoPiiValues()
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var entry = Assert.Single(bundle.Entries);
        var piiExample = Assert.Single(entry.Examples, example => example.Name == "pii-negative");

        var result = new Cp6CloudEventValidator(bundle).Validate(File.ReadAllBytes(bundle.GetAssetPath(piiExample.Path)));
        var renderedResult = JsonSerializer.Serialize(result);

        Assert.False(result.IsValid);
        Assert.Equal(Cp6EventValidationFailure.SchemaMismatch, result.Failure);
        Assert.DoesNotContain("must-not-appear", renderedResult, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("example.invalid", renderedResult, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UnknownSchemaVersion_FailsBeforeCloudEventDecode()
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var valid = File.ReadAllText(bundle.GetAssetPath(Assert.Single(bundle.Entries).Examples[0].Path));
        var changed = valid.Replace("\"schemaversion\": \"1.0.0\"", "\"schemaversion\": \"1.1.0\"", StringComparison.Ordinal);

        var result = new Cp6CloudEventValidator(bundle).Validate(Encoding.UTF8.GetBytes(changed));

        Assert.False(result.IsValid);
        Assert.Equal(Cp6EventValidationFailure.UnknownContract, result.Failure);
        Assert.Null(result.CloudEvent);
    }

    [Fact]
    public void BundleLoad_RejectsAssetHashDrift()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), $"cp6-contract-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            foreach (var sourcePath in Directory.GetFiles(ContractRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(ContractRoot, sourcePath);
                var targetPath = Path.Combine(temporaryRoot, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
                File.Copy(sourcePath, targetPath);
            }

            var validPath = Path.Combine(
                temporaryRoot,
                "events",
                "platform",
                "contract-example-changed",
                "v1",
                "examples",
                "valid.json");
            File.AppendAllText(validPath, " ");

            Assert.Throws<InvalidDataException>(() => Cp6ContractBundle.Load(temporaryRoot));
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData("\"type\"", "\"type\":\"unknown\",")]
    [InlineData("\"type\"", "\"\\u0074ype\":\"unknown\",")]
    [InlineData("\"resourceId\"", "\"resourceId\":\"ignored\",")]
    [InlineData("\"resourceId\"", "\"traceparent\":\"first\",\"traceparent\":\"second\",")]
    public void DuplicateProperties_FailBeforeInterpretation(string marker, string duplicate)
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var raw = File.ReadAllText(bundle.GetAssetPath(Assert.Single(bundle.Entries).Examples[0].Path));
        var changed = raw.Insert(raw.IndexOf(marker, StringComparison.Ordinal), duplicate);
        var result = new Cp6CloudEventValidator(bundle).Validate(Encoding.UTF8.GetBytes(changed));
        Assert.False(result.IsValid);
        Assert.Equal(Cp6EventValidationFailure.MalformedJson, result.Failure);
        Assert.Null(result.CloudEvent);
    }

    [Fact]
    public void CorrectEventTypeWithWrongSchemaId_IsRejected()
    {
        var bundle = Cp6ContractBundle.Load(ContractRoot);
        var entry = Assert.Single(bundle.Entries);
        var raw = File.ReadAllText(bundle.GetAssetPath(entry.Examples[0].Path));
        var changed = raw.Replace(entry.SchemaId, "https://contracts.cp6.uk/events/platform/other/v1/schema.json", StringComparison.Ordinal);
        Assert.NotEqual(raw, changed);
        var result = new Cp6CloudEventValidator(bundle).Validate(Encoding.UTF8.GetBytes(changed));
        Assert.Equal(Cp6EventValidationFailure.UnknownContract, result.Failure);
        Assert.Null(result.CloudEvent);
    }

    [Fact]
    public void PinnedBundle_RequiresExactManifestAndVersion()
    {
        var bytes = File.ReadAllBytes(Path.Combine(ContractRoot, Cp6ContractBundle.IndexFileName));
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var pinned = Cp6ContractBundle.Load(ContractRoot, "1.0.0", hash);
        Assert.Equal("1.0.0", pinned.BundleVersion);
        Assert.Throws<InvalidDataException>(() => Cp6ContractBundle.Load(ContractRoot, "1.1.0", hash));
        Assert.Throws<InvalidDataException>(() => Cp6ContractBundle.Load(ContractRoot, "1.0.0", new string('0', 64)));
        WithCopiedBundle(root =>
        {
            File.AppendAllText(Path.Combine(root, Cp6ContractBundle.IndexFileName), " ");
            Assert.Throws<InvalidDataException>(() => Cp6ContractBundle.Load(root, "1.0.0", hash));
        });
    }

    [Fact]
    public void Manifest_DuplicatePropertiesAreRejectedEvenWithoutExplicitPin()
    {
        WithCopiedBundle(root =>
        {
            var path = Path.Combine(root, Cp6ContractBundle.IndexFileName);
            var raw = File.ReadAllText(path);
            File.WriteAllText(path, raw.Insert(raw.IndexOf('"'), "\"bundleVersion\":\"9.0.0\","));
            Assert.Throws<JsonException>(() => Cp6ContractBundle.Load(root));
        });
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("manifest")]
    [InlineData("duplicate-type")]
    public void OriginalBundleRules_RejectSchemaHashMetadataAndDuplicateType(string change)
    {
        WithCopiedBundle(root =>
        {
            var path = Path.Combine(root, Cp6ContractBundle.IndexFileName);
            var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            var entry = manifest["entries"]![0]!;
            if (change == "schema") File.AppendAllText(Path.Combine(root, entry["schemaPath"]!.GetValue<string>()), " ");
            else if (change == "manifest") entry["schemaId"] = "https://contracts.cp6.uk/events/platform/wrong/v1/schema.json";
            else manifest["entries"]!.AsArray().Add(entry.DeepClone());
            File.WriteAllText(path, manifest.ToJsonString());
            var error = Record.Exception(() => Cp6ContractBundle.Load(root));
            Assert.True(error is InvalidDataException or Json.Schema.JsonSchemaException);
        });
    }

    private static void WithCopiedBundle(Action<string> test)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cp6-s01-bundle-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            foreach (var source in Directory.GetFiles(ContractRoot, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(root, Path.GetRelativePath(ContractRoot, source));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target);
            }
            test(root);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string FindContractRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var solution = Path.Combine(current.FullName, "CP6.Platform.sln");
            var contracts = Path.Combine(current.FullName, "contracts");
            if (File.Exists(solution) && Directory.Exists(contracts))
            {
                return contracts;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6.Platform contract bundle.");
    }
}
