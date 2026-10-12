using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Messaging.NativeContracts;

namespace CP6.Platform.UnitTests;

public sealed class NativeProfileContractTests
{
    private static readonly string[] Profiles =
        ["strict-json", "null-only", "json-semantic-equality", "crm-inq-r3-v15", "erp-credit-current-v1", "erp-credit-frozen-r8"];

    private static byte[] Read(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "S01", name));
    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    private static string Text(JsonElement value, string name) => value.GetProperty(name).GetString()!;
    private static Cp6NativeContractCatalog Catalog() => Cp6NativeContractCatalog.LoadS01(Read("profile-inputs.json"));

    public static IEnumerable<object[]> Vectors()
    {
        using var document = JsonDocument.Parse(Read("vectors.json"));
        return document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(item => Profiles.Contains(Text(item, "profile_id"), StringComparer.Ordinal))
            .Select(item => new object[] { Text(item, "id"), item.Clone() }).ToArray();
    }

    [Fact]
    public void ImportedPackage_HasExactSourceBytesAndAllDeclaredCases()
    {
        Assert.Equal("2c221a04333cd5e0beba8d12a7aecbb08cbc80caa5faa74b22313045ce420e5e", Hash(Read("profile-inputs.json")));
        Assert.Equal("b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01", Hash(Read("vectors.json")));
        var cases = Vectors().Select(row => (JsonElement)row[1]).ToArray();
        Assert.Equal(35, cases.Length);
        Assert.Equal(35, cases.Select(item => Text(item, "id")).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(new[] { 7, 7, 7, 4, 5, 5 }, Profiles.Select(profile => cases.Count(item => Text(item, "profile_id") == profile)));
    }

    [Theory]
    [MemberData(nameof(Vectors))]
    public void FixedVector_UsesProductionBoundary(string id, JsonElement vector)
    {
        Assert.Equal(id, Text(vector, "id"));
        var input = vector.GetProperty("input");
        var expected = vector.GetProperty("expected");
        var profile = Text(vector, "profile_id");
        switch (Text(vector, "operation"))
        {
            case "strict_json_parse":
                CheckAccepted(expected, () => { using var document = Cp6NativeJson.Parse(Utf8(input.GetString()!)); });
                break;
            case "required_null_property":
                CheckAccepted(expected, () => Cp6NativeJson.RequireNullProperty(Utf8(Text(input, "raw_json")), Text(input, "required_property")));
                break;
            case "json_semantic_equality":
                Assert.Equal(expected.GetProperty("equal").GetBoolean(), Cp6NativeJson.SemanticallyEquals(
                    Utf8(Text(input, "left_raw_json")), Utf8(Text(input, "right_raw_json"))));
                break;
            case "native_schema_identity":
                CheckAccepted(expected, () => Catalog().ResolveOpenApi(profile, new(
                    Text(input, "openapi"), Text(input, "title"), Text(input, "version"),
                    Text(input, "schema_root"), Text(input, "schema_sha256")), Read("crm-inq.openapi.json")));
                break;
            case "native_registry_identity":
                CheckAccepted(expected, () => Catalog().ResolveRegistry(profile, new(
                    Text(input, "codec_namespace"), Text(input, "schema_file"), Text(input, "schema_sha256")),
                    Read(profile == "erp-credit-current-v1" ? "erp-current.registry.json" : "erp-frozen.registry.json")));
                break;
            case "original_utf8_retention":
                var originalBytes = Utf8(Text(input, "original_utf8"));
                var replayBytes = Utf8(Text(input, "replay_utf8"));
                var original = Cp6OriginalJson.Capture(originalBytes);
                var replay = Cp6OriginalJson.Capture(replayBytes);
                Assert.Equal(originalBytes, original.GetBytes());
                Assert.Equal(Text(expected, "original_sha256"), original.Sha256);
                Assert.Equal(Text(expected, "replay_sha256"), replay.Sha256);
                Assert.Equal(expected.GetProperty("bytes_equal").GetBoolean(), original.Matches(replayBytes));
                Assert.Equal(expected.GetProperty("digest_equal").GetBoolean(), original.Sha256 == replay.Sha256);
                Assert.True(Cp6NativeJson.SemanticallyEquals(originalBytes, replayBytes));
                break;
            default:
                Assert.Fail($"Unexecuted operation for {id}");
                break;
        }
    }

    [Fact]
    public void Catalog_RejectsManifestBytesOrPackageVersionDrift()
    {
        var original = Read("profile-inputs.json");
        Assert.Throws<InvalidDataException>(() => Cp6NativeContractCatalog.LoadS01(original.Append((byte)' ').ToArray()));
        var changed = Utf8(Encoding.UTF8.GetString(original).Replace("CP6-S01-CONTRACT-INPUTS/1.0.0", "CP6-S01-CONTRACT-INPUTS/9.0.0", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => Cp6NativeContractCatalog.LoadS01(changed));
    }

    [Theory]
    [InlineData("crm-inq-r3-v15", "crm-inq.openapi.json")]
    [InlineData("erp-credit-current-v1", "erp-current.registry.json")]
    [InlineData("erp-credit-frozen-r8", "erp-frozen.registry.json")]
    public void IdentityRoutes_VerifyActualAssetBytesAndRejectUnknownProfile(string profile, string asset)
    {
        var catalog = Catalog();
        var bytes = Read(asset);
        var identityCase = Vectors().Select(row => (JsonElement)row[1]).First(item =>
            Text(item, "profile_id") == profile && item.GetProperty("expected").TryGetProperty("accepted", out var accepted) && accepted.GetBoolean());
        var input = identityCase.GetProperty("input");
        string Resolve(string selected, byte[] raw) => profile == "crm-inq-r3-v15"
            ? catalog.ResolveOpenApi(selected, new(Text(input, "openapi"), Text(input, "title"), Text(input, "version"), Text(input, "schema_root"), Text(input, "schema_sha256")), raw)
            : catalog.ResolveRegistry(selected, new(Text(input, "codec_namespace"), Text(input, "schema_file"), Text(input, "schema_sha256")), raw);
        Assert.Equal(profile, Resolve(profile, bytes));
        Assert.Throws<InvalidDataException>(() => Resolve(profile, [.. bytes, (byte)' ']));
        Assert.Throws<InvalidDataException>(() => Resolve("latest", bytes));
        Assert.Throws<InvalidDataException>(() => Resolve(profile.ToUpperInvariant(), bytes));
        Assert.Throws<InvalidDataException>(() => Resolve(profile == "erp-credit-current-v1" ? "erp-credit-frozen-r8" : "erp-credit-current-v1", bytes));
    }

    [Fact]
    public void OriginalBytes_AreOwnedAndNoMutableViewEscapes()
    {
        var input = Utf8("{ \"word\":\"漢字\", \"amount\":-0.000 }\r\n");
        var expected = input.ToArray();
        var snapshot = Cp6OriginalJson.Capture(input);
        input[0] = (byte)'!';
        var returned = snapshot.GetBytes();
        returned[0] = (byte)'?';
        Assert.Equal(expected, snapshot.GetBytes());
        Assert.Equal(Hash(expected), snapshot.Sha256);
        Assert.True(snapshot.Matches(expected));
        Assert.False(snapshot.Matches(input));
        Assert.False(snapshot.Matches(Utf8("{\"amount\":0,\"word\":\"漢字\"}")));
    }

    [Theory]
    [InlineData("{\"a\":1,\"\\u0061\":1}")]
    [InlineData("{\"nested\":[{\"secret\":0,\"secret\":1}]}")]
    [InlineData("{\"traceparent\":\"first\",\"traceparent\":\"second\"}")]
    [InlineData("{\"a\":1,}")]
    [InlineData("/*comment*/{}")]
    [InlineData("")]
    public void InvalidJson_CannotBeCapturedOrCompared(string json)
    {
        Assert.ThrowsAny<JsonException>(() => Cp6OriginalJson.Capture(Utf8(json)));
        Assert.ThrowsAny<JsonException>(() => Cp6NativeJson.SemanticallyEquals(Utf8(json), Utf8("{}")));
    }

    [Fact]
    public void NullProjection_RejectsEscapedDuplicatesAndNonObjectRoot()
    {
        Assert.Throws<JsonException>(() => Cp6NativeJson.RequireNullProperty(Utf8("{\"fixedNull\":null,\"\\u0066ixedNull\":null}"), "fixedNull"));
        Assert.Throws<JsonException>(() => Cp6NativeJson.RequireNullProperty(Utf8("null"), "fixedNull"));
    }

    [Fact]
    public void InvalidUtf8StringValue_IsRejectedBeforeByteCapture()
    {
        byte[] invalid = [(byte)'{', (byte)'"', (byte)'a', (byte)'"', (byte)':', (byte)'"', 0xff, (byte)'"', (byte)'}'];
        Assert.ThrowsAny<JsonException>(() => Cp6OriginalJson.Capture(invalid));
    }

    [Fact]
    public void ParsedDocument_DoesNotBorrowMutableCallerMemory()
    {
        var bytes = Utf8("{\"a\":1}");
        using var document = Cp6NativeJson.Parse(bytes);
        bytes[5] = (byte)'2';
        Assert.Equal(1, document.RootElement.GetProperty("a").GetInt32());
    }

    [Theory]
    [InlineData("1e1000", "1e1001")]
    [InlineData("12345678901234567890123456789012345678", "12345678901234567890123456789012345679")]
    [InlineData("{\"a\":null}", "{}")]
    public void SchemaEquality_DoesNotRoundDistinctNumbersOrDropNull(string left, string right)
    {
        Assert.False(Cp6NativeJson.SemanticallyEquals(Utf8(left), Utf8(right)));
    }

    private static void CheckAccepted(JsonElement expected, Action operation)
    {
        if (expected.GetProperty("accepted").GetBoolean()) operation();
        else Assert.True(Record.Exception(operation) is JsonException or InvalidDataException, "Expected a contract rejection.");
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
