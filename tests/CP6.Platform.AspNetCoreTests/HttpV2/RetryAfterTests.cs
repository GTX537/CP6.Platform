using CP6.Platform.AspNetCore.HttpV2;

namespace CP6.Platform.AspNetCoreTests.HttpV2;

public sealed class RetryAfterTests
{
    private static readonly DateTimeOffset Observed = new(1994, 11, 6, 8, 49, 32, TimeSpan.Zero);

    [Theory]
    [InlineData("Sun, 06 Nov 1994 08:49:37 GMT")]
    [InlineData("Sunday, 06-Nov-94 08:49:37 GMT")]
    [InlineData("Sun Nov  6 08:49:37 1994")]
    [InlineData("\t5 \t")]
    public void SingleHttpValue_UsesInvariantThreeDateFormsAndOws(string value)
    {
        var result = Cp6RetryAfter.Parse([value], Observed);
        Assert.Equal(Cp6RetryAfterKind.Valid, result.Kind);
        Assert.Equal(TimeSpan.FromSeconds(5), result.Delay);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("1, 2")]
    [InlineData("+1")]
    [InlineData("１")]
    [InlineData("\n1")]
    [InlineData("Sun, 32 Nov 1994 08:49:37 GMT")]
    public void InvalidValues_AreNotServerWaits(string value) =>
        Assert.Equal(Cp6RetryAfterKind.InvalidRetryAfter, Cp6RetryAfter.Parse([value], Observed).Kind);

    [Fact]
    public void DuplicateFields_AreInvalid() => Assert.Equal(Cp6RetryAfterKind.InvalidRetryAfter,
        Cp6RetryAfter.Parse(["1", "1"], Observed).Kind);

    [Theory]
    [InlineData("60")]
    [InlineData("999999999999999999999999999999999999")]
    public void HugeLegalInteger_StopsInsteadOfFallingBack(string value) =>
        Assert.Equal(Cp6RetryAfterKind.StopServerWaitExceeded, Cp6RetryAfter.Parse([value], Observed).Kind);

    [Fact]
    public void MissingValue_HasNoServerWait() => Assert.Equal(Cp6RetryAfterKind.Absent,
        Cp6RetryAfter.Parse(null, Observed).Kind);

    [Theory]
    [InlineData("Sunday, 06-Nov-٩٤ 08:49:37 GMT")]
    [InlineData("Sunday, ٠٦-Nov-94 08:49:37 GMT")]
    public void UnicodeDateDigitsAreInvalidWithoutThrowing(string value) => Assert.Equal(Cp6RetryAfterKind.InvalidRetryAfter,
        Cp6RetryAfter.Parse([value], Observed).Kind);

    [Fact]
    public void LeapSecondPreservesFutureServerWait()
    {
        var time = new DateTimeOffset(2016, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var result = Cp6RetryAfter.Parse(["Sat, 31 Dec 2016 23:59:60 GMT"], time);
        Assert.Equal(Cp6RetryAfterKind.Valid, result.Kind); Assert.Equal(TimeSpan.FromSeconds(1), result.Delay);
    }

    [Fact]
    public void ExtremeObservedUtcDoesNotOverflowRfc850Pivot()
    {
        var result = Cp6RetryAfter.Parse(["Sunday, 06-Nov-94 08:49:37 GMT"], DateTimeOffset.MaxValue);
        Assert.NotEqual(Cp6RetryAfterKind.StopServerWaitExceeded, result.Kind);
    }

    [Fact]
    public void FutureRfc850BeyondFiftyYearsUsesMatchingPastCentury()
    {
        var result = Cp6RetryAfter.Parse(["Wednesday, 06-Nov-75 08:49:37 GMT"], new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        // 2075 is >50 years away; 1975 is Thursday, so a false weekday cannot silently parse.
        Assert.Equal(Cp6RetryAfterKind.InvalidRetryAfter, result.Kind);
        var valid = Cp6RetryAfter.Parse(["Thursday, 06-Nov-75 08:49:37 GMT"], new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(Cp6RetryAfterKind.Valid, valid.Kind); Assert.Equal(TimeSpan.Zero, valid.Delay);
    }
}
