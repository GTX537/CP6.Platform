using System.Text;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class DeterministicJsonTests
{
    [Fact]
    public void Canonicalize_sorts_properties_and_emits_exact_utf8_without_newline()
    {
        var actual = Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"z\":2,\"a\":\"é\",\"control\":\"\\n\"}"));
        var expected = Encoding.UTF8.GetBytes("{\"a\":\"é\",\"control\":\"\\u000a\",\"z\":2}");
        Assert.Equal(expected, actual);
        Assert.NotEqual((byte)'\n', actual[^1]);
    }

    [Theory]
    [InlineData("{\"a\":1,\"a\":2}", "duplicate-property")]
    [InlineData("{\"a\":1,}", "invalid-json")]
    [InlineData("/*x*/{\"a\":1}", "invalid-json")]
    [InlineData("{\"n\":-1}", "number-format")]
    [InlineData("{\"n\":1.0}", "number-format")]
    [InlineData("{\"n\":1e0}", "number-format")]
    public void Canonicalize_rejects_non_profile_json(string json, string code)
    {
        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(code, exception.Code);
    }

    [Fact]
    public void Canonicalize_rejects_bom_non_nfc_and_resource_limit_violations()
    {
        Assert.Equal("utf8-bom", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize([0xef, 0xbb, 0xbf, (byte)'{', (byte)'}'])).Code);
        Assert.Equal("unicode-normalization", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"v\":\"é\"}"))).Code);
        Assert.Equal("object-size", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(new byte[4 * 1024 * 1024 + 1])).Code);
    }

    [Fact]
    public void Canonicalize_rejects_invalid_utf8_and_unpaired_unicode_escape()
    {
        var malformedUtf8 = new byte[] { (byte)'{', (byte)'\"', (byte)'v', (byte)'\"', (byte)':', (byte)'\"', 0xc3, 0x28, (byte)'\"', (byte)'}' };
        Assert.Equal("invalid-utf8", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(malformedUtf8)).Code);
        Assert.Equal("unicode-scalar", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"v\":\"\\uD800\"}"))).Code);
    }

    [Fact]
    public void Canonicalize_rejects_non_object_root_and_depth_33()
    {
        Assert.Equal("root-object", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("[]"))).Code);

        var nested = new StringBuilder();
        for (var index = 0; index < 33; index++) nested.Append("{\"a\":");
        nested.Append('0');
        for (var index = 0; index < 33; index++) nested.Append('}');
        Assert.Equal("depth-limit", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(nested.ToString()))).Code);
    }

    [Fact]
    public void Canonicalize_rejects_member_array_and_string_limits()
    {
        var members = "{" + string.Join(',', Enumerable.Range(0, 257).Select(index => $"\"p{index:D3}\":0")) + "}";
        Assert.Equal("member-limit", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(members))).Code);

        var array = "{\"a\":[" + string.Join(',', Enumerable.Repeat("0", 4097)) + "]}";
        Assert.Equal("array-limit", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(array))).Code);

        var largeString = "{\"v\":\"" + new string('a', 65_537) + "\"}";
        Assert.Equal("string-limit", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes(largeString))).Code);
    }

    [Fact]
    public void Canonicalize_accepts_int64_max_and_rejects_larger_integer()
    {
        var accepted = Encoding.UTF8.GetBytes("{\"n\":9223372036854775807}");
        Assert.Equal(accepted, Cp6DeterministicJson.Canonicalize(accepted));
        Assert.Equal("integer-range", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6DeterministicJson.Canonicalize(Encoding.UTF8.GetBytes("{\"n\":9223372036854775808}"))).Code);
    }

    [Fact]
    public void Golden_fixture_has_exact_cross_process_bytes_and_hash()
    {
        var root = FindRoot();
        var fixtureRoot = Path.Combine(root, "contracts", "release", "v1", "fixtures", "deterministic");
        var actual = Cp6DeterministicJson.Canonicalize(File.ReadAllBytes(Path.Combine(fixtureRoot, "simple.input.json")));
        Assert.Equal(File.ReadAllBytes(Path.Combine(fixtureRoot, "simple.canonical.json")), actual);
        Assert.Equal("c70d0dc4eaf50a576944851115bb0e81a935fc39c7893a992a6dd00092eafdb1", Cp6DeterministicJson.Sha256Hex(actual));
    }

    private static string FindRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            if (File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln"))) return current.FullName;
        }

        throw new DirectoryNotFoundException();
    }
}
