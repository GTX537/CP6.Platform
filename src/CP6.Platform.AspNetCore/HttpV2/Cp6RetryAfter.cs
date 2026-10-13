using System.Globalization;
using System.Text.RegularExpressions;

namespace CP6.Platform.AspNetCore.HttpV2;

public enum Cp6RetryAfterKind { Absent, Valid, InvalidRetryAfter, StopServerWaitExceeded }
public readonly record struct Cp6RetryAfter(Cp6RetryAfterKind Kind, TimeSpan Delay)
{
    public static Cp6RetryAfter Parse(IEnumerable<string>? values, DateTimeOffset observedUtc)
    {
        if (values is null) return default;
        var fields = values.Take(2).ToArray();
        if (fields.Length == 0) return default;
        if (fields.Length != 1) return Invalid;
        var value = fields[0].Trim(' ', '\t');
        if (value.Length == 0) return Invalid;
        if (value.Any(c => c > 127)) return Invalid;
        if (value.All(c => c is >= '0' and <= '9'))
        {
            // Accumulate only to the cap: arbitrarily long legal integers never become invalid.
            long seconds = 0;
            foreach (var digit in value)
            {
                seconds = seconds * 10 + digit - '0';
                if (seconds > 30) return Exceeded;
            }
            return new(Cp6RetryAfterKind.Valid, TimeSpan.FromSeconds(seconds));
        }

        // HTTP allows a leap second. Parse the preceding representable second, then advance once.
        var leapSecond = Regex.IsMatch(value, @"[0-9]{2}:[0-9]{2}:60(?: |$)", RegexOptions.CultureInvariant);
        if (leapSecond) value = Regex.Replace(value, @"(?<=[0-9]{2}:[0-9]{2}):60(?= |$)", ":59", RegexOptions.CultureInvariant);
        const DateTimeStyles style = DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        DateTimeOffset parsed;
        var rfc850 = Regex.Match(value, @"^(Monday|Tuesday|Wednesday|Thursday|Friday|Saturday|Sunday), (\d{2}-[A-Za-z]{3}-)(\d{2})( \d{2}:\d{2}:\d{2} GMT)$", RegexOptions.CultureInvariant);
        if (rfc850.Success)
        {
            var year = observedUtc.Year / 100 * 100 + int.Parse(rfc850.Groups[3].Value, CultureInfo.InvariantCulture);
            if (year < observedUtc.Year - 50) year += 100;
            // Ignore weekday until the two-digit year has been resolved.
            var date = rfc850.Groups[2].Value + year.ToString("D4", CultureInfo.InvariantCulture) + rfc850.Groups[4].Value;
            if (!DateTimeOffset.TryParseExact(date, "dd-MMM-yyyy HH:mm:ss 'GMT'", CultureInfo.InvariantCulture, style, out parsed)) return Invalid;
            var futureLimit = observedUtc.Year <= 9949 ? observedUtc.AddYears(50) : DateTimeOffset.MaxValue;
            if (parsed > futureLimit)
            {
                if (parsed.Year <= 100) return Invalid;
                parsed = parsed.AddYears(-100);
            }
            if (!string.Equals(parsed.ToString("dddd", CultureInfo.InvariantCulture), rfc850.Groups[1].Value, StringComparison.Ordinal)) return Invalid;
        }
        else if (!DateTimeOffset.TryParseExact(value,
                     ["ddd, dd MMM yyyy HH:mm:ss 'GMT'", "ddd MMM  d HH:mm:ss yyyy", "ddd MMM dd HH:mm:ss yyyy"],
                     CultureInfo.InvariantCulture, style, out parsed)) return Invalid;
        if (leapSecond)
        {
            if (parsed > DateTimeOffset.MaxValue.AddSeconds(-1)) return Exceeded;
            parsed = parsed.AddSeconds(1);
        }
        var delay = Math.Max(0, (double)decimal.Ceiling((decimal)(parsed - observedUtc).Ticks / TimeSpan.TicksPerMillisecond));
        return delay > 30000 ? Exceeded : new(Cp6RetryAfterKind.Valid, TimeSpan.FromMilliseconds(delay));
    }

    private static Cp6RetryAfter Invalid => new(Cp6RetryAfterKind.InvalidRetryAfter, TimeSpan.Zero);
    private static Cp6RetryAfter Exceeded => new(Cp6RetryAfterKind.StopServerWaitExceeded, TimeSpan.Zero);
}
