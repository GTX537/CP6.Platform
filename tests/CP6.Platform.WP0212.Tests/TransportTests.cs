using CP6.Platform.Messaging;
using CP6.Platform.Messaging.TransportV1;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CP6.Platform.WP0212.Tests;

public sealed class TransportTests
{
    private static readonly string Root = FindRoot();
    private static readonly Cp6ContractBundle Bundle = Cp6ContractBundle.LoadForTransport(Path.Combine(Root, "contracts"), "1.0.0",
        Hash(File.ReadAllBytes(Path.Combine(Root, "contracts", Cp6ContractBundle.IndexFileName))));
    private static readonly Cp6ContractBundleEntry Entry = Bundle.Entries.Single();
    private static byte[] Original() => File.ReadAllBytes(Bundle.GetAssetPath(Entry.Examples.Single(e => e.Name == "valid").Path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static TransportRouteV1 Route(string? hash = null, int max = 1048576) => new(
        TransportRouteV1.GenericProfile, "fixture", Entry.EventType, Entry.SchemaId, Entry.SchemaVersion,
        hash ?? Entry.SchemaSha256, Bundle, "platform", "urn:cp6:platform",
        "cp6.platform.contract-example-changed.v1", max);
    private static AuthenticatedTransportFactsV1 Facts(byte[] bytes) => new("platform",
        Guid.Parse("11111111-1111-4111-8111-111111111111"), "na", "cp6.platform.contract-example-changed.v1",
        "11111111-1111-4111-8111-111111111111/example-1", "evt-0001", "example-1", 7,
        "corr-0001", "cmd-0001", "independent-test-native-reference", Hash(bytes), Cp6CloudEventCodec.StructuredContentType);
    private static TransportRegistryV1 Registry(ITransportAuthorityV1 authority, int hostMax = 1048576, int brokerMax = 1048576)
        => new([Route()], hostMax, brokerMax, authority);
    private static ValueTask<TransportQualificationV1> Qualify(TransportRegistryV1 registry, byte[] raw,
        TransportPurposeV1 purpose = TransportPurposeV1.Publish, object? evidence = null)
        => registry.QualifyAsync(TransportRouteV1.GenericProfile, "fixture", raw, evidence ?? new object(), purpose);

    [Fact]
    public void RegistrationPinsExactHashAndRejectsDuplicateEvenWhenIdentical()
    {
        Assert.Throws<ArgumentException>(() => Route(new string('0', 64)));
        var route = Route();
        Assert.Throws<ArgumentException>(() => new TransportRegistryV1([route, route], 1048576, 1048576, new Authority(Facts(Original()))));
    }

    [Fact]
    public void NativeC02AdoptionIsDisabled()
        => Assert.Throws<ArgumentException>(() => new TransportRouteV1("C02", "fixture", Entry.EventType,
            Entry.SchemaId, Entry.SchemaVersion, Entry.SchemaSha256, Bundle, "platform", "urn:cp6:platform", "cp6.platform.events.v1"));

    [Fact]
    public async Task SeparatePinnedVersionsCoexistWithoutLatestFallback()
    {
        var next = VersionBundle("1.0.1"); var nextEntry = next.Entries.Single();
        var route = new TransportRouteV1(TransportRouteV1.GenericProfile, "fixture", nextEntry.EventType,
            nextEntry.SchemaId, nextEntry.SchemaVersion, nextEntry.SchemaSha256, next, "platform", "urn:cp6:platform", Route().Topic);
        foreach (var version in new[] { "1.0.0", "1.0.1", "1.0.2" })
        {
            var json = System.Text.Json.Nodes.JsonNode.Parse(Original())!; json["schemaversion"] = version;
            var raw = Encoding.UTF8.GetBytes(json.ToJsonString());
            var registry = new TransportRegistryV1([Route(), route], 1048576, 1048576, new Authority(Facts(raw)));
            var result = await Qualify(registry, raw);
            Assert.Equal(version != "1.0.2", result.IsQualified);
            if (result.IsQualified) Assert.Equal(version, result.Message!.Route.SchemaVersion);
        }
    }

    [Fact]
    public void TransportBundleRejectsExternalReferenceBeforeSchemaBuildAndDoesNotAlterLegacyLoader()
    {
        Assert.Throws<InvalidDataException>(() => VersionBundle("1.0.1", true));
        var legacy = Cp6ContractBundle.Load(Path.Combine(Root, "contracts"));
        Assert.True(new Cp6CloudEventValidator(legacy).Validate(Original()).IsValid);
        Assert.Throws<ArgumentException>(() => new TransportRouteV1(TransportRouteV1.GenericProfile, "fixture", Entry.EventType,
            Entry.SchemaId, Entry.SchemaVersion, Entry.SchemaSha256, legacy, "platform", "urn:cp6:platform", Route().Topic));
    }

    [Fact]
    public void SameTupleWithDifferentHashAndAmbiguousRoutePoliciesFailAtStartup()
    {
        var other = VersionBundle("1.0.0"); var entry = other.Entries.Single();
        var route = new TransportRouteV1(TransportRouteV1.GenericProfile, "fixture", entry.EventType,
            entry.SchemaId, entry.SchemaVersion, entry.SchemaSha256, other, "platform", "urn:cp6:platform", Route().Topic);
        Assert.NotEqual(Route().SchemaSha256, route.SchemaSha256);
        Assert.Throws<ArgumentException>(() => new TransportRegistryV1([Route(), route], 1048576, 1048576, new Authority(null)));
        var next = VersionBundle("1.0.1"); var nextEntry = next.Entries.Single();
        var ambiguous = new TransportRouteV1(TransportRouteV1.GenericProfile, "fixture", nextEntry.EventType,
            nextEntry.SchemaId, nextEntry.SchemaVersion, nextEntry.SchemaSha256, next, "platform", "urn:cp6:other", Route().Topic);
        Assert.Throws<ArgumentException>(() => new TransportRegistryV1([Route(), ambiguous], 1048576, 1048576, new Authority(null)));
    }

    [Theory]
    [InlineData("producer")][InlineData("tenant")][InlineData("region")][InlineData("topic")]
    [InlineData("key")][InlineData("id")][InlineData("aggregate")][InlineData("version")]
    [InlineData("hash")][InlineData("metadata")][InlineData("media")][InlineData("correlation")][InlineData("causation")]
    public async Task IndependentFactsMustMatchEveryEnvelopeAndRouteIdentity(string field)
    {
        var bytes = Original(); var facts = Facts(bytes);
        facts = field switch
        {
            "producer" => facts with { Producer = "attacker" }, "tenant" => facts with { TenantId = Guid.NewGuid() },
            "region" => facts with { Region = "eu" }, "topic" => facts with { Topic = "wrong" },
            "key" => facts with { PartitionKey = facts.PartitionKey.Replace('/', ':') }, "id" => facts with { MessageId = "wrong" },
            "aggregate" => facts with { AggregateId = "wrong" }, "version" => facts with { AggregateVersion = 8 },
            "hash" => facts with { RawEnvelopeSha256 = new string('0', 64) }, "metadata" => facts with { NativeReference = "" },
            "media" => facts with { ContentType = "application/json" }, "correlation" => facts with { CorrelationId = "wrong" },
            _ => facts with { CausationId = "wrong" }
        };
        Assert.False((await Qualify(Registry(new Authority(facts)), bytes)).IsQualified);
    }

    [Theory]
    [InlineData("type", "unknown.type.v1")][InlineData("dataschema", "https://attacker.invalid/schema")]
    [InlineData("schemaversion", "1.0.1")]
    public async Task ExactTupleHasNoFallback(string property, string replacement)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(Original())!; json[property] = replacement;
        var bytes = Encoding.UTF8.GetBytes(json.ToJsonString());
        Assert.False((await Qualify(Registry(new Authority(Facts(bytes))), bytes)).IsQualified);
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task UnknownSchemaAndMalformedRawCanOnlyBeQuarantinedAfterAuthentication(bool malformed)
    {
        var bytes = malformed ? "{malformed"u8.ToArray() : Encoding.UTF8.GetBytes(
            Encoding.UTF8.GetString(Original()).Replace("\"schemaversion\": \"1.0.0\"", "\"schemaversion\": \"9.0.0\""));
        var registry = Registry(new Authority(Facts(bytes)));
        var result = await Qualify(registry, bytes, TransportPurposeV1.Receive);
        Assert.False(result.IsQualified); Assert.NotNull(result.Message);
        Assert.Equal(TransportDecisionV1.DefinitelyRejected, await registry.PublishAsync(result.Message!, new Transport(TransportDecisionV1.Confirmed)));
        Assert.Equal(QuarantineDecisionV1.Drop, await registry.QuarantineAsync(result.Message!, "unknown-contract-v1", new Store()));
        Assert.Null((await Qualify(Registry(new Authority(null)), bytes, TransportPurposeV1.Receive)).Message);
    }

    [Fact]
    public async Task ProfileAndHeaderCannotSelectAuthority()
    {
        var raw = Original(); var authority = new Authority(null); var registry = Registry(authority);
        Assert.False((await Qualify(registry, raw, evidence: Facts(raw))).IsQualified);
        var denied = await registry.QualifyAsync("forged-profile", "fixture", raw, new object(), TransportPurposeV1.Receive);
        Assert.False(denied.IsQualified); Assert.Equal(1, authority.Calls);
    }

    [Theory]
    [InlineData(1, 1048576)][InlineData(1048576, 1)]
    public async Task EffectiveLimitRejectsBeforeAuthority(int host, int broker)
    {
        var raw = Original(); var authority = new Authority(Facts(raw));
        Assert.False((await Qualify(Registry(authority, host, broker), raw)).IsQualified);
        Assert.Equal(0, authority.Calls);
    }

    [Fact]
    public async Task RawBytesAreOwnedAndRetryPreservesOriginalIdentity()
    {
        var raw = Original(); var expected = raw.ToArray(); var registry = Registry(new Authority(Facts(raw)));
        var result = await Qualify(registry, raw); Assert.True(result.IsQualified);
        raw[0] = 0; var exposed = result.Message!.CopyRawBytes(); exposed[0] = 0;
        var transport = new Transport(TransportDecisionV1.Unknown);
        Assert.Equal(TransportDecisionV1.Unknown, await registry.PublishAsync(result.Message, transport));
        transport.Decision = TransportDecisionV1.Confirmed;
        Assert.Equal(TransportDecisionV1.Confirmed, await registry.PublishAsync(result.Message, transport));
        Assert.All(transport.Calls, m => Assert.Equal(expected, m.CopyRawBytes()));
        Assert.All(transport.Calls, m => Assert.Equal("evt-0001", m.Facts.MessageId));
    }

    [Fact]
    public async Task ExceptionsCancellationForeignTokenAndReceiveTokenNeverConfirm()
    {
        var raw = Original(); var registry = Registry(new Authority(Facts(raw)));
        var token = (await Qualify(registry, raw)).Message!;
        var throwing = new Transport(TransportDecisionV1.Confirmed) { Fail = true };
        Assert.Equal(TransportDecisionV1.Unknown, await registry.PublishAsync(token, throwing));
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.Equal(TransportDecisionV1.Unknown, await registry.PublishAsync(token, new Transport(TransportDecisionV1.Confirmed), cts.Token));
        var other = Registry(new Authority(Facts(raw)));
        Assert.Equal(TransportDecisionV1.DefinitelyRejected, await other.PublishAsync(token, throwing));
        var receive = (await Qualify(registry, raw, TransportPurposeV1.Receive)).Message!;
        Assert.Equal(TransportDecisionV1.DefinitelyRejected, await registry.PublishAsync(receive, throwing));
    }

    [Fact]
    public async Task DuplicateBusinessFieldsRejectButRootTraceCompatibilityIsPreserved()
    {
        var original = Encoding.UTF8.GetString(Original());
        foreach (var (injection, expected) in new[] { ("\"id\":\"duplicate\",", false), ("\"traceparent\":\"bad\",\"traceparent\":\"bad2\",", true) })
        {
            var raw = Encoding.UTF8.GetBytes(original.Insert(original.IndexOf('{') + 1, injection));
            Assert.Equal(expected, (await Qualify(Registry(new Authority(Facts(raw))), raw)).IsQualified);
        }
    }

    [Fact]
    public async Task DiagnosticsNeverExposePayloadPropertySecretsAndDepthIsBounded()
    {
        var raw = Encoding.UTF8.GetBytes("{\"secret-token-should-never-escape\":" + new string('[', 65) + "0" + new string(']', 65) + "}");
        var result = await Qualify(Registry(new Authority(Facts(raw))), raw);
        Assert.False(result.IsQualified); Assert.InRange(result.Diagnostics.Count, 1, 32);
        Assert.All(result.Diagnostics, d => { Assert.InRange(d.Length, 1, 256); Assert.DoesNotContain("secret", d); });
    }

    [Theory]
    [InlineData("missing")][InlineData("bytes")][InlineData("policy")][InlineData("id")][InlineData("route")][InlineData("hash")]
    [InlineData("native-missing")][InlineData("native-key")][InlineData("native-reference")]
    public async Task QuarantineRequiresIndependentExactReadback(string fault)
    {
        var raw = Original(); var registry = Registry(new Authority(Facts(raw)));
        var token = (await Qualify(registry, raw, TransportPurposeV1.Receive)).Message!;
        var store = new Store { Fault = fault };
        Assert.Equal(QuarantineDecisionV1.Retry, await registry.QuarantineAsync(token, "quarantine-v1", store));
        Assert.Equal(1, store.Reads);
        store.Fault = "";
        Assert.Equal(QuarantineDecisionV1.Drop, await registry.QuarantineAsync(token, "quarantine-v1", store));
        Assert.Equal(raw, store.Saved!.CopyRawBytes());
        Assert.Equal(token.Facts, store.Saved.NativeFacts);
    }

    [Fact]
    public async Task RepeatedInvalidBodyAtDistinctNativeReferencesHasIndependentReadback()
    {
        var raw = "{malformed"u8.ToArray();
        var store = new DeliveryHistoryStore();
        foreach (var reference in new[] { "topic:partition:40", "topic:partition:41" })
        {
            var facts = Facts(raw) with { NativeReference = reference };
            var registry = Registry(new Authority(facts));
            var result = await Qualify(registry, raw, TransportPurposeV1.Receive);
            Assert.False(result.IsQualified); Assert.NotNull(result.Message);
            Assert.Equal(QuarantineDecisionV1.Drop, await registry.QuarantineAsync(result.Message!, "quarantine-v1", store));
        }
        Assert.Equal(2, store.Records.Count);
        Assert.All(store.Records.Values, record => Assert.Equal(raw, record.CopyRawBytes()));
    }

    [Theory]
    [InlineData(false)][InlineData(true)]
    public async Task AuthenticatedWrongNativeKeyCanOnlyQuarantineItsActualMetadata(bool unknownSchema)
    {
        var raw = unknownSchema ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(Original()).Replace(
            "\"schemaversion\": \"1.0.0\"", "\"schemaversion\": \"9.0.0\"")) : Original();
        var facts = Facts(raw) with { PartitionKey = "actual-wrong-native-key" };
        var registry = Registry(new Authority(facts));
        var result = await Qualify(registry, raw, TransportPurposeV1.Receive);
        Assert.False(result.IsQualified); Assert.NotNull(result.Message);
        var store = new Store();
        Assert.Equal(QuarantineDecisionV1.Drop, await registry.QuarantineAsync(result.Message!, "quarantine-v1", store));
        Assert.Equal(facts.PartitionKey, store.Saved!.NativeFacts!.PartitionKey);
        Assert.Equal(TransportDecisionV1.DefinitelyRejected,
            await registry.PublishAsync(result.Message!, new Transport(TransportDecisionV1.Confirmed)));
        Assert.False((await Qualify(registry, raw, TransportPurposeV1.Publish)).IsQualified);
    }

    private sealed class DeliveryHistoryStore : ITransportQuarantineStoreV1
    {
        public Dictionary<(string Profile, string Route, string Policy, string Id, string Hash, string NativeReference), QuarantineRecordV1> Records { get; } = [];
        public ValueTask PersistAsync(QuarantineRecordV1 original, CancellationToken cancellationToken)
        {
            Records.TryAdd((original.Profile, original.Route, original.Policy, original.MessageId,
                original.RawEnvelopeSha256, original.NativeFacts!.NativeReference), original);
            return ValueTask.CompletedTask;
        }
        public ValueTask<QuarantineRecordV1?> ReadCommittedAsync(string profile, string route, string policy,
            string messageId, string rawEnvelopeSha256, string nativeReference, CancellationToken cancellationToken)
            => ValueTask.FromResult(Records.TryGetValue((profile, route, policy, messageId, rawEnvelopeSha256, nativeReference),
                out var record) ? record : null);
    }

    private sealed class Authority(AuthenticatedTransportFactsV1? facts) : ITransportAuthorityV1
    {
        public int Calls;
        public ValueTask<AuthenticatedTransportFactsV1?> AuthenticateAsync(TransportAuthenticationRequestV1 request, object hostEvidence, CancellationToken cancellationToken)
        { Calls++; return ValueTask.FromResult(facts); }
    }
    private sealed class Transport(TransportDecisionV1 decision) : IMessageTransportV1
    {
        public TransportDecisionV1 Decision = decision; public bool Fail; public List<VerifiedTransportMessageV1> Calls = [];
        public ValueTask<TransportDecisionV1> PublishAsync(VerifiedTransportMessageV1 message, CancellationToken cancellationToken)
        { Calls.Add(message); if (Fail) throw new IOException("secret error"); return ValueTask.FromResult(Decision); }
    }
    private sealed class Store : ITransportQuarantineStoreV1
    {
        public string Fault = ""; public int Reads; public QuarantineRecordV1? Saved;
        public ValueTask PersistAsync(QuarantineRecordV1 original, CancellationToken cancellationToken) { Saved = original; return ValueTask.CompletedTask; }
        public ValueTask<QuarantineRecordV1?> ReadCommittedAsync(string profile, string route, string policy, string messageId, string rawEnvelopeSha256, string nativeReference, CancellationToken cancellationToken)
        {
            Reads++; if (Fault == "missing") return ValueTask.FromResult<QuarantineRecordV1?>(null);
            return ValueTask.FromResult<QuarantineRecordV1?>(new(profile, Fault == "route" ? "other" : route,
                Fault == "policy" ? "other" : policy, Fault == "id" ? "other" : messageId,
                Fault == "hash" ? new string('0', 64) : rawEnvelopeSha256,
                Fault == "bytes" ? "wrong"u8.ToArray() : Saved!.CopyRawBytes(),
                Fault switch
                {
                    "native-missing" => null,
                    "native-key" => Saved!.NativeFacts! with { PartitionKey = "wrong" },
                    "native-reference" => Saved!.NativeFacts! with { NativeReference = "wrong" },
                    _ => Saved!.NativeFacts
                }));
        }
    }
    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "contracts", Cp6ContractBundle.IndexFileName))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException();
    }

    private static Cp6ContractBundle VersionBundle(string version, bool externalReference = false)
    {
        // Keep every generated input under the task evidence directory; no temporary cleanup hides failures.
        var root = Path.Combine(Root, "docs", "development", "evidence", "WP02-12", "tests", "library",
            "bundle-inputs", DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        foreach (var path in Entry.Examples.Select(e => e.Path).Append(Entry.SchemaPath))
        {
            var destination = Path.Combine(root, path); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(Bundle.GetAssetPath(path), destination);
        }
        var schema = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(root, Entry.SchemaPath)))!;
        schema["properties"]!["schemaversion"]!["const"] = version;
        schema["description"] = "WP02-12 test-only version isolation";
        if (externalReference) schema["$ref"] = "https://must-never-fetch.invalid/schema";
        var schemaBytes = Encoding.UTF8.GetBytes(schema.ToJsonString()); File.WriteAllBytes(Path.Combine(root, Entry.SchemaPath), schemaBytes);
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllBytes(Path.Combine(Root, "contracts", Cp6ContractBundle.IndexFileName)))!;
        manifest["entries"]![0]!["schemaVersion"] = version; manifest["entries"]![0]!["schemaSha256"] = Hash(schemaBytes);
        var manifestBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString()); File.WriteAllBytes(Path.Combine(root, Cp6ContractBundle.IndexFileName), manifestBytes);
        return Cp6ContractBundle.LoadForTransport(root, "1.0.0", Hash(manifestBytes));
    }
    [Fact]
    public void ExplicitTransportBoundaryIsAvailableWithoutChangingLegacyDefault()
    {
        var type = typeof(Cp6ContractBundle).Assembly.GetType("CP6.Platform.Messaging.TransportV1.TransportRegistryV1");
        Assert.NotNull(type);
    }
}
