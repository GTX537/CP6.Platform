using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace CP6.Platform.Contracts.SloQualificationV1;

/// <summary>Generic exact arithmetic, not a financial input contract. Limits are evidence resource limits.</summary>
public sealed class ExactNumber : IComparable<ExactNumber>
{
    private ExactNumber(BigInteger numerator, BigInteger denominator, BigInteger? coefficient, int? scale)
    {
        if (denominator <= 0) throw new ArgumentOutOfRangeException(nameof(denominator));
        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
        Coefficient = coefficient;
        Scale = scale;
    }

    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }
    public BigInteger? Coefficient { get; }
    public int? Scale { get; }
    public static ExactNumber FromRational(BigInteger numerator, BigInteger denominator) => new(numerator, denominator, null, null);
    public static ExactNumber Parse(string lexical)
    {
        ArgumentNullException.ThrowIfNull(lexical);
        if (lexical.Length > 16384) throw new ArgumentException("Evidence numeric token exceeds resource bound.");
        var match = Regex.Match(lexical, @"\A(-?)(0|[1-9][0-9]*)(?:\.([0-9]+))?(?:[eE]([+-]?[0-9]+))?\z", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        if (!match.Success) throw new FormatException("Expected an exact JSON number token.");
        var exponent = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : 0;
        if (exponent is < -16384 or > 16384) throw new ArgumentOutOfRangeException(nameof(lexical), "Evidence exponent resource bound.");
        var coefficient = BigInteger.Parse(match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value, CultureInfo.InvariantCulture);
        var scale = checked(match.Groups[3].Length - exponent);
        return scale >= 0
            ? new(coefficient, BigInteger.Pow(10, scale), coefficient, scale)
            : new(coefficient * BigInteger.Pow(10, -scale), BigInteger.One, coefficient, scale);
    }

    public int CompareTo(ExactNumber? other) => other is null ? 1 : (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);
    public bool NumericEquals(ExactNumber other) => CompareTo(other) == 0;
    public static ExactNumber operator +(ExactNumber left, ExactNumber right) => FromRational(left.Numerator * right.Denominator + right.Numerator * left.Denominator, left.Denominator * right.Denominator);
    public static ExactNumber operator *(ExactNumber left, ExactNumber right) => FromRational(left.Numerator * right.Numerator, left.Denominator * right.Denominator);
    public static ExactNumber operator /(ExactNumber left, ExactNumber right)
    {
        if (right.Numerator.IsZero) throw new DivideByZeroException();
        var sign = right.Numerator.Sign;
        return FromRational(left.Numerator * right.Denominator * sign, left.Denominator * BigInteger.Abs(right.Numerator));
    }
    public override string ToString() => $"{Numerator.ToString(CultureInfo.InvariantCulture)}/{Denominator.ToString(CultureInfo.InvariantCulture)}";
}
