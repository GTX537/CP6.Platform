using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Contracts.SloQualificationV1;
using CP6.Platform.Testing.EvidenceV1;

namespace CP6.Platform.WP0226.Tests;

public class ExactTests
{
    [Theory]
    [InlineData("9007199254740993", "9007199254740992")]
    [InlineData("999999999999999999999999999999.12345678", "999999999999999999999999999999.12345677")]
    [InlineData("999999.123456789012", "999999.123456789011")]
    public void Exact_assertions_distinguish_large_or_fine_values(string left, string right)
    { Assert.Throws<InvalidOperationException>(() => ExactAssert.Equal(left, right)); }

    [Theory]
    [InlineData("-0.00", "0e1000")]
    [InlineData("1.000", "1")]
    [InlineData("1e1000", "10e999")]
    public void Numeric_equality_does_not_erase_original_lexical_or_bytes(string left, string right)
    {
        var a = ExactValue.FromUtf8(Encoding.UTF8.GetBytes(left));
        var b = ExactValue.FromUtf8(Encoding.UTF8.GetBytes(right));
        ExactAssert.Equal(a.Number, b.Number);
        Assert.NotEqual(a.OriginalLexical, b.OriginalLexical);
        Assert.Throws<InvalidOperationException>(() => ExactAssert.BytesEqual(a.OriginalBytes, b.OriginalBytes));
        var copy = a.OriginalBytes; copy[0] ^= 1;
        Assert.Equal(left, Encoding.UTF8.GetString(a.OriginalBytes));
    }
    [Fact]
    public void Nonterminating_rational_is_never_rounded()
    {
        var third = ExactValue.FromRational(1, 3);
        Assert.Null(third.Number.Scale);
        ExactAssert.Equal(ExactNumber.Parse("1"), third.Number + third.Number + third.Number);
        Assert.False(third.Number.NumericEquals(ExactNumber.Parse("0.3333333333333333333333333333")));
    }
    [Fact]
    public void Clr_kind_and_binary_rational_are_preserved()
    {
        var integer = ExactValue.FromClr(9007199254740993L);
        Assert.Equal("System.Int64", integer.ClrKind);
        ExactAssert.Equal(ExactNumber.Parse("9007199254740993"), integer.Number);
        var dec = ExactValue.FromClr(1.2300m);
        Assert.Equal("System.Decimal", dec.ClrKind);
        Assert.Equal(4, dec.Number.Scale);
        Assert.Equal("System.Single", ExactValue.FromClr(0.1f).ClrKind);
        var binary = ExactValue.FromClr(0.1d);
        Assert.False(binary.Number.NumericEquals(ExactNumber.Parse("0.1")));
        Assert.Equal(BigInteger.Parse("3602879701896397", CultureInfo.InvariantCulture), binary.Number.Numerator);
        Assert.Equal(BigInteger.Parse("36028797018963968", CultureInfo.InvariantCulture), binary.Number.Denominator);
        Assert.Throws<ArgumentException>(() => ExactValue.FromClr(double.NaN));
        Assert.NotEqual(ExactValue.FromClr(-0.0d).OriginalBytes, ExactValue.FromClr(0.0d).OriginalBytes);
    }
    [Fact]
    public void Pinned_S01_vectors_are_consumed_as_arithmetic_provenance_not_owner_acceptance()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "vectors.json"));
        Assert.Equal("b9a63a1512da2d2374e95d49d9f69c8636d06465f88a47862dc3604605d5be01", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var doc = JsonDocument.Parse(bytes);
        var consumed = new List<string>();
        foreach (var item in doc.RootElement.GetProperty("cases").EnumerateArray())
        {
            var expected = item.GetProperty("expected");
            if (item.GetProperty("input").ValueKind != JsonValueKind.String || !expected.TryGetProperty("pair", out var pair) || pair.ValueKind != JsonValueKind.Object) continue;
            var input = item.GetProperty("input").GetString()!;
            var coefficientText = pair.TryGetProperty("unscaledValue", out var credit) ? credit.GetString()! : pair.GetProperty("unscaledDigits").GetString()!;
            var coefficient = BigInteger.Parse(coefficientText, CultureInfo.InvariantCulture);
            var scale = pair.GetProperty("scale").GetInt32();
            ExactAssert.Equal(ExactNumber.FromRational(coefficient, BigInteger.Pow(10, scale)), ExactNumber.Parse(input));
            if (pair.TryGetProperty("originalWire", out var wire)) ExactAssert.BytesEqual(Encoding.UTF8.GetBytes(wire.GetString()!), ExactValue.FromUtf8(Encoding.UTF8.GetBytes(input)).OriginalBytes);
            consumed.Add(item.GetProperty("id").GetString()!);
        }
        Assert.Contains("NUM-003", consumed); // FIN-CREDIT 38/8; full 38-digit coefficient.
        Assert.Contains("NUM-013", consumed); // FIN-INV signed: arbitrary integer digits, scale 12.
        Assert.Contains("INV-003", consumed); // FIN-INV unsigned: scale 18.
        Assert.True(consumed.Count >= 10);
    }

    [Fact]
    public void Oversized_wire_token_is_rejected_before_UTF8_string_allocation()
    {
        var bytes = new byte[1_000_000];
        Array.Fill(bytes, (byte)'1');
        var before = GC.GetAllocatedBytesForCurrentThread();
        Assert.ThrowsAny<ArgumentException>(() => ExactValue.FromUtf8(bytes));
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 100000, $"Oversized lexical input allocated {allocated} bytes before rejection.");
    }
}
