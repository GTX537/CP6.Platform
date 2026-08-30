using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Contracts;
using Json.Schema;

namespace CP6.Platform.UnitTests;

public sealed class SloEvidenceContractTests
{
    private const string PiiSentinel = "must-not-appear@example.invalid";
    private static readonly string ContractRoot = FindContractRoot();
    private static readonly string SloRoot = Path.Combine(ContractRoot, "observability", "slo-evidence", "v1");

    [Fact]
    public void SchemaExamples_MatchDeclaredOutcomes()
    {
        var schemaText = File.ReadAllText(Path.Combine(SloRoot, "schema.json"));
        var schema = JsonSchema.FromText(schemaText, new BuildOptions { Dialect = Dialect.Draft202012 });
        var examples = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["valid-pass.json"] = true,
            ["partial-indeterminate.json"] = true,
            ["non-candidate-indeterminate.json"] = true,
            ["pii-negative.json"] = false
        };

        foreach (var (fileName, expectedValid) in examples)
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(SloRoot, "examples", fileName)));
            var result = schema.Evaluate(
                document.RootElement,
                new EvaluationOptions
                {
                    OutputFormat = OutputFormat.List,
                    RequireFormatValidation = true
                });

            Assert.True(
                result.IsValid == expectedValid,
                $"Fixture '{fileName}' expected valid={expectedValid} at {result.InstanceLocation}.");
            Assert.DoesNotContain(PiiSentinel, result.ToString(), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("valid-pass.json", Cp6SloEvidenceResult.Pass)]
    [InlineData("partial-indeterminate.json", Cp6SloEvidenceResult.Indeterminate)]
    [InlineData("non-candidate-indeterminate.json", Cp6SloEvidenceResult.Indeterminate)]
    public void ValidExamples_ParseAndMatchEvaluator(string fileName, Cp6SloEvidenceResult expected)
    {
        var evidence = Cp6SloEvidenceDocument.Parse(
            File.ReadAllBytes(Path.Combine(SloRoot, "examples", fileName)));

        Assert.Equal(expected, evidence.Result);
        Assert.Equal(expected, Cp6SloEvidenceEvaluator.Evaluate(evidence));
        Assert.False(evidence.ProductionSloClaimed);
    }

    [Fact]
    public void Parse_RejectsDuplicateJsonPropertiesWithoutEchoingValues()
    {
        var valid = File.ReadAllText(Path.Combine(SloRoot, "examples", "valid-pass.json"));
        var duplicate = valid.Replace(
            "\"schemaVersion\": \"1.0.0\",",
            "\"schemaVersion\": \"1.0.0\", \"schemaVersion\": \"secret-duplicate\",",
            StringComparison.Ordinal);

        var exception = Assert.Throws<JsonException>(
            () => Cp6SloEvidenceDocument.Parse(Encoding.UTF8.GetBytes(duplicate)));

        Assert.DoesNotContain("secret-duplicate", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_RejectsClaimedPassWhenSourceDigestDrifts()
    {
        var valid = File.ReadAllText(Path.Combine(SloRoot, "examples", "valid-pass.json"));
        var drifted = valid.Replace(
            $"\"releaseArtifactDigest\": \"{Digest('b')}\"",
            $"\"releaseArtifactDigest\": \"{Digest('8')}\"",
            StringComparison.Ordinal);

        var exception = Assert.Throws<JsonException>(
            () => Cp6SloEvidenceDocument.Parse(Encoding.UTF8.GetBytes(drifted)));

        Assert.DoesNotContain(Digest('8'), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AssetManifest_DetectsSchemaOrFixtureDrift()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(SloRoot, "assets.v1.json")));
        var assets = manifest.RootElement.GetProperty("assets").EnumerateArray().ToArray();

        Assert.Equal(5, assets.Length);
        foreach (var asset in assets)
        {
            var relativePath = asset.GetProperty("path").GetString()!;
            var expected = asset.GetProperty("sha256").GetString()!;
            var actual = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(SloRoot, relativePath))))
                .ToLowerInvariant();

            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(true, Cp6SloEvidenceCompleteness.Complete, true, Cp6SloEvidenceResult.Pass)]
    [InlineData(false, Cp6SloEvidenceCompleteness.Complete, true, Cp6SloEvidenceResult.Indeterminate)]
    [InlineData(true, Cp6SloEvidenceCompleteness.Partial, true, Cp6SloEvidenceResult.Indeterminate)]
    [InlineData(true, Cp6SloEvidenceCompleteness.Complete, false, Cp6SloEvidenceResult.Fail)]
    public void Evaluate_UsesFailClosedMatrix(
        bool candidate,
        Cp6SloEvidenceCompleteness completeness,
        bool thresholdMet,
        Cp6SloEvidenceResult expected)
    {
        var evidence = CreateEvidence(candidate, completeness, thresholdMet);

        Assert.Equal(expected, Cp6SloEvidenceEvaluator.Evaluate(evidence));
    }

    [Fact]
    public void Evaluate_ReturnsIndeterminateForZeroSamplesOrCoverageGap()
    {
        var baseline = CreateEvidence(true, Cp6SloEvidenceCompleteness.Complete, true);

        Assert.Equal(
            Cp6SloEvidenceResult.Indeterminate,
            Cp6SloEvidenceEvaluator.Evaluate(
                baseline with { Measurement = baseline.Measurement with { SampleCount = 0 } }));
        Assert.Equal(
            Cp6SloEvidenceResult.Indeterminate,
            Cp6SloEvidenceEvaluator.Evaluate(
                baseline with { Window = baseline.Window with { ObservedCoverage = 0.99m } }));
    }

    [Fact]
    public void Evaluate_ReturnsIndeterminateForDefinitionReleaseOrExclusionDrift()
    {
        var baseline = CreateEvidence(true, Cp6SloEvidenceCompleteness.Complete, true);
        var source = Assert.Single(baseline.Sources);

        Assert.Equal(
            Cp6SloEvidenceResult.Indeterminate,
            Cp6SloEvidenceEvaluator.Evaluate(
                baseline with
                {
                    Sources = [source with { DefinitionDigest = Digest('9') }]
                }));
        Assert.Equal(
            Cp6SloEvidenceResult.Indeterminate,
            Cp6SloEvidenceEvaluator.Evaluate(
                baseline with
                {
                    Sources = [source with { ReleaseArtifactDigest = Digest('8') }]
                }));
        Assert.Equal(
            Cp6SloEvidenceResult.Indeterminate,
            Cp6SloEvidenceEvaluator.Evaluate(
                baseline with
                {
                    Measurement = baseline.Measurement with { ExcludedCount = 1 },
                    Sources = [source with { ExclusionsVerified = false }]
                }));
    }

    [Fact]
    public void ReleaseIdentity_SerializesOnlyTheApprovedWireShape()
    {
        var json = JsonSerializer.Serialize(CreateEvidence(true, Cp6SloEvidenceCompleteness.Complete, true).Release);
        using var document = JsonDocument.Parse(json);
        var propertyNames = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "artifactDigest", "candidate", "contractBundleDigest", "gitSha", "service", "version" },
            propertyNames);
        Assert.DoesNotContain("mode", propertyNames);
    }

    private static Cp6SloEvidenceDocument CreateEvidence(
        bool candidate,
        Cp6SloEvidenceCompleteness completeness,
        bool thresholdMet)
    {
        var artifactDigest = candidate ? Digest('b') : string.Empty;
        var release = new Cp6ReleaseIdentity(
            "crm-api",
            candidate ? "0.8.0-alpha.1" : "local",
            candidate ? new string('a', 40) : string.Empty,
            artifactDigest,
            candidate ? Digest('c') : string.Empty,
            candidate ? Cp6ReleaseMode.Candidate : Cp6ReleaseMode.NonCandidate);
        var sli = new Cp6SloIndicator(
            "http-availability",
            "1.0.0",
            Digest('d'),
            "ratio",
            Cp6SloAggregation.Ratio,
            Cp6SloComparator.GreaterThanOrEqual,
            0.99m);
        var source = new Cp6SloEvidenceSource(
            "synthetic-test",
            "service-a-to-b",
            sli.DefinitionDigest,
            artifactDigest,
            Digest('e'),
            Digest('f'),
            true);

        return new Cp6SloEvidenceDocument(
            Cp6SloEvidenceDocument.CurrentSchemaVersion,
            "evidence-20260830-001",
            new DateTimeOffset(2026, 8, 30, 7, 0, 0, TimeSpan.Zero),
            release,
            sli,
            new Cp6SloEvidenceWindow(
                new DateTimeOffset(2026, 8, 30, 6, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 8, 30, 7, 0, 0, TimeSpan.Zero),
                1m,
                1m),
            new Cp6SloMeasurement(
                100,
                thresholdMet ? 0.995m : 0.98m,
                thresholdMet ? 99.5m : 98m,
                100m,
                null,
                0),
            [source],
            completeness,
            Cp6SloEvidenceResult.Indeterminate,
            false);
    }

    private static string Digest(char value) => $"sha256:{new string(value, 64)}";

    private static string FindContractRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var path = Path.Combine(current.FullName, "contracts");
            if (Directory.Exists(path) && File.Exists(Path.Combine(current.FullName, "CP6.Platform.sln")))
            {
                return path;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the CP6.Platform contracts directory.");
    }
}
