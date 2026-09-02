using System.Text;
using CP6.Platform.Release;

namespace CP6.Platform.ReleaseTests;

public sealed class TrustAndStorageValidationTests
{
    private const string RevokedKeyId = "sha256:1c00b4b4d7d0720310aea46bf702975c1ce6e11b56701571f64fc1e15988662f";
    private const string CurrentKeyId = "sha256:ff836d88c0b5df6950e3f6755768ff3385e683376e006e4d19c1fc407d8fc74b";

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
            policy.RequireKey(RevokedKeyId, "candidate-locator", 3, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
        var historical = policy.EvaluateHistoricalKey(RevokedKeyId, "candidate-locator", 3, signedAt, evaluatedAt);
        Assert.True(historical.WasValidAtSigning);
        Assert.True(historical.CurrentlyRevoked);
        Assert.Equal("trust-policy-downgrade", Assert.Throws<Cp6ReleaseContractException>(() =>
            policy.RequireKey(CurrentKeyId, "candidate-locator", 1, signedAt, evaluatedAt, Cp6ReleaseValidationMode.Current)).Code);
    }

    [Fact]
    public void Trust_policy_rejects_a_key_id_that_does_not_match_the_public_key_spki()
    {
        var json = Encoding.UTF8.GetString(
            ReleaseTestData.Fixture("supporting", "trust.valid.json"));
        json = json.Replace(CurrentKeyId, "sha256:" + new string('0', 64), StringComparison.Ordinal);

        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedTrustPolicy.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("trust-key", exception.Code);
    }

    [Fact]
    public void Trust_policy_rejects_malformed_public_key_pem()
    {
        var json = Encoding.UTF8.GetString(
            ReleaseTestData.Fixture("supporting", "trust.valid.json"));
        var start = json.IndexOf("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal);
        var end = json.IndexOf("-----END PUBLIC KEY-----", start, StringComparison.Ordinal) +
                  "-----END PUBLIC KEY-----".Length;
        json = string.Concat(json.AsSpan(0, start), "not-a-public-key", json.AsSpan(end));

        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedTrustPolicy.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("trust-key", exception.Code);
    }

    [Fact]
    public void Trust_policy_rejects_noncanonical_public_key_line_endings()
    {
        var json = Encoding.UTF8.GetString(
            ReleaseTestData.Fixture("supporting", "trust.valid.json"));
        json = json.Replace("\\u000a", "\\u000d\\u000a", StringComparison.Ordinal);

        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedTrustPolicy.Parse(Encoding.UTF8.GetBytes(json)));

        Assert.Equal("trust-key", exception.Code);
    }

    [Fact]
    public void Storage_authority_retains_the_complete_pinned_r2_mapping()
    {
        var policy = Cp6PinnedTrustPolicy.Parse(
            ReleaseTestData.Fixture("supporting", "trust.valid.json"));

        var authority = policy.RequireStorageAuthority("cp6-release-r2-v1");

        Assert.Equal("cloudflare-r2", authority.Provider);
        Assert.Equal("11111111111111111111111111111111", authority.AccountId);
        Assert.Equal("default", authority.Jurisdiction);
        Assert.Equal(
            "https://{accountId}.r2.cloudflarestorage.com",
            authority.EndpointTemplate);
        Assert.Equal(
            "https://11111111111111111111111111111111.r2.cloudflarestorage.com",
            authority.Endpoint);
        Assert.Equal("cp6-release", authority.Bucket);
        Assert.Equal(
            new[] { "candidates/platform/", "objects/sha256/" },
            authority.AllowedPrefixes);
        Assert.Equal("AuthenticatedReadConditionalCreate", authority.AccessMode);
        Assert.Equal(4 * 1024 * 1024, authority.MaxObjectBytes);
        var dictionary = Assert.IsAssignableFrom<
            IDictionary<string, Cp6PinnedStorageAuthority>>(
            policy.StorageAuthorities);
        Assert.Throws<NotSupportedException>(() =>
            dictionary.Add("untrusted-r2", authority));
        Assert.Equal(
            "storage-authority",
            Assert.Throws<Cp6ReleaseContractException>(() =>
                policy.RequireStorageAuthority("untrusted-r2")).Code);
    }

    [Fact]
    public void Storage_authority_rejects_an_unapproved_prefix()
    {
        var exception = Assert.Throws<Cp6ReleaseContractException>(() =>
            Cp6PinnedTrustPolicy.Parse(ReleaseTestData.Fixture(
                "supporting",
                "trust-authority.invalid.json")));

        Assert.Equal("storage-authority", exception.Code);
    }
}
