using System.Globalization;
using System.Numerics;
using System.Text;
using CP6.Platform.Contracts.SloQualificationV1;

namespace CP6.Platform.Testing.EvidenceV1;

public sealed class ExactValue
{
    private readonly byte[] originalBytes;
    private ExactValue(string kind, string lexical, byte[] bytes, ExactNumber number)
    { ClrKind = kind; OriginalLexical = lexical; originalBytes = (byte[])bytes.Clone(); Number = number; }
    public string ClrKind { get; }
    public string OriginalLexical { get; }
    public byte[] OriginalBytes => (byte[])originalBytes.Clone();
    internal int ByteLength => originalBytes.Length;
    // Allocation-free representation accounting; shared numerator/coefficient backing is counted twice conservatively.
    internal long NumericBackingBytes => checked((long)Number.Numerator.GetByteCount() + Number.Denominator.GetByteCount() +
        (Number.Coefficient is { } coefficient ? coefficient.GetByteCount() : 0));
    public ExactNumber Number { get; }
    public static ExactValue FromUtf8(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 16384) throw new ArgumentException("Evidence numeric token exceeds resource bound.");
        var lexical = new UTF8Encoding(false, true).GetString(bytes);
        return new("Utf8Number", lexical, bytes.ToArray(), ExactNumber.Parse(lexical));
    }
    public static ExactValue FromRational(BigInteger numerator, BigInteger denominator)
    {
        var number = ExactNumber.FromRational(numerator, denominator);
        return new("BigIntegerRational", number.ToString(), Encoding.UTF8.GetBytes(number.ToString()), number);
    }
    public static ExactValue FromClr<T>(T value) where T : struct
    {
        object boxed = value;
        var kind = typeof(T).FullName!;
        // IEEE values are captured as their exact binary rational, never via a rounded decimal comparison.
        if (boxed is double d) return Binary(kind, d.ToString("R", CultureInfo.InvariantCulture), BitConverter.GetBytes(d), BitConverter.DoubleToInt64Bits(d), 52, 11, 1023);
        if (boxed is float f) return Binary(kind, f.ToString("R", CultureInfo.InvariantCulture), BitConverter.GetBytes(f), (uint)BitConverter.SingleToInt32Bits(f), 23, 8, 127);
        if (boxed is not (byte or sbyte or short or ushort or int or uint or long or ulong or decimal or BigInteger)) throw new ArgumentException("Unsupported exact CLR numeric kind.");
        var lexical = ((IFormattable)boxed).ToString(null, CultureInfo.InvariantCulture);
        var bytes = boxed is decimal dec ? decimal.GetBits(dec).SelectMany(BitConverter.GetBytes).ToArray() : Encoding.UTF8.GetBytes(lexical);
        return new(kind, lexical, bytes, ExactNumber.Parse(lexical));
    }
    private static ExactValue Binary(string kind, string lexical, byte[] bytes, long bits, int fractionBits, int exponentBits, int bias)
    {
        var raw = unchecked((ulong)bits);
        var fraction = raw & ((1UL << fractionBits) - 1);
        var exponent = (int)((raw >> fractionBits) & ((1UL << exponentBits) - 1));
        if (exponent == (1 << exponentBits) - 1) throw new ArgumentException("Non-finite metrics cannot qualify.");
        BigInteger numerator = exponent == 0 ? fraction : (1UL << fractionBits) | fraction;
        var power = (exponent == 0 ? 1 - bias : exponent - bias) - fractionBits;
        if ((raw & (1UL << (fractionBits + exponentBits))) != 0) numerator = -numerator;
        var number = power >= 0 ? ExactNumber.FromRational(numerator << power, BigInteger.One) : ExactNumber.FromRational(numerator, BigInteger.One << -power);
        return new(kind, lexical, bytes, number);
    }
}

public static class ExactAssert
{
    public static void Equal(string expected, string actual) => Equal(ExactNumber.Parse(expected), ExactNumber.Parse(actual));
    public static void Equal(ExactNumber expected, ExactNumber actual)
    { if (!expected.NumericEquals(actual)) throw new InvalidOperationException("Exact numeric values differ."); }
    public static void BytesEqual(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    { if (!expected.SequenceEqual(actual)) throw new InvalidOperationException("Original bytes differ."); }
}
