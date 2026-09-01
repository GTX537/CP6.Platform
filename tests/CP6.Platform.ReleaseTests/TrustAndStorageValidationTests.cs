using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class TrustAndStorageValidationTests
{
    [Fact]
    public void Platform_tag_derives_fixed_locator_and_bundle_keys()
    {
        var keys = Cp6CandidateLocatorKeys.ForPlatformTag("v0.10.0-test.1");
        Assert.Equal("candidates/platform/v0.10.0-test.1/candidate-locator.v1.json", keys.LocatorKey);
        Assert.Equal("candidates/platform/v0.10.0-test.1/candidate-locator.v1.sigstore.json", keys.BundleKey);
    }

    [Theory]
    [InlineData("v01.10.0")]
    [InlineData("v0.10.0/escape")]
    [InlineData("v0.10.0..x")]
    [InlineData(" V0.10.0")]
    public void Unsafe_or_noncanonical_tags_are_rejected(string tag)
    {
        Assert.Equal("release-tag", Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6CandidateLocatorKeys.ForPlatformTag(tag)).Code);
    }

    [Fact]
    public void Current_mode_rejects_revoked_and_policy_downgrade_while_audit_reports_history()
    {
        var policy = Cp6PinnedTrustPolicy.Parse(ReleaseTestData.Fixture("supporting", "trust.revoked.valid.json"));
        var signedAt = DateTimeOffset.Parse("2026-07-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture);
        var evaluatedAt = DateTimeOffset.Parse("2026-09-01T00:00:00.000Z", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal("trust-revoked", Assert.Throws<Cp6ReleaseContractException>(() =>
            policy.RequireKey("sha256:" + new string('a', 64), "candidate-locator", 3, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
        var historical = policy.EvaluateHistoricalKey("sha256:" + new string('a', 64), "candidate-locator", 3, signedAt, evaluatedAt);
        Assert.True(historical.WasValidAtSigning);
        Assert.True(historical.CurrentlyRevoked);
        Assert.Equal("trust-policy-downgrade", Assert.Throws<Cp6ReleaseContractException>(() =>
            policy.RequireKey("sha256:" + new string('b', 64), "candidate-locator", 1, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
    }
}
