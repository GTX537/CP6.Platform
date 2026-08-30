using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CP6.Platform.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<Cp6SloAggregation>))]
public enum Cp6SloAggregation
{
    Ratio,
    Percentile,
    Gauge
}

[JsonConverter(typeof(JsonStringEnumConverter<Cp6SloComparator>))]
public enum Cp6SloComparator
{
    GreaterThanOrEqual,
    LessThanOrEqual
}

[JsonConverter(typeof(JsonStringEnumConverter<Cp6SloEvidenceCompleteness>))]
public enum Cp6SloEvidenceCompleteness
{
    Complete,
    Partial,
    Missing
}

[JsonConverter(typeof(JsonStringEnumConverter<Cp6SloEvidenceResult>))]
public enum Cp6SloEvidenceResult
{
    Pass,
    Fail,
    Indeterminate
}

public sealed record Cp6SloIndicator(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("definitionVersion")] string DefinitionVersion,
    [property: JsonPropertyName("definitionDigest")] string DefinitionDigest,
    [property: JsonPropertyName("unit")] string Unit,
    [property: JsonPropertyName("aggregation")] Cp6SloAggregation Aggregation,
    [property: JsonPropertyName("comparator")] Cp6SloComparator Comparator,
    [property: JsonPropertyName("threshold")] decimal Threshold)
{
    [JsonIgnore]
    public bool HasValidDefinitionDigest => Cp6SloContractGuard.IsDigest(DefinitionDigest);

    internal void Validate()
    {
        Cp6SloContractGuard.Identifier(Id, nameof(Id));
        Cp6SloContractGuard.SemanticVersion(DefinitionVersion, nameof(DefinitionVersion));
        Cp6SloContractGuard.Identifier(Unit, nameof(Unit));
        if (!Enum.IsDefined(Aggregation))
        {
            throw new ArgumentOutOfRangeException(nameof(Aggregation), "SLI aggregation is not supported.");
        }

        if (!Enum.IsDefined(Comparator))
        {
            throw new ArgumentOutOfRangeException(nameof(Comparator), "SLI comparator is not supported.");
        }
    }
}

public sealed record Cp6SloEvidenceWindow(
    [property: JsonPropertyName("startUtc")] DateTimeOffset StartUtc,
    [property: JsonPropertyName("endUtc")] DateTimeOffset EndUtc,
    [property: JsonPropertyName("expectedCoverage")] decimal ExpectedCoverage,
    [property: JsonPropertyName("observedCoverage")] decimal ObservedCoverage)
{
    internal void Validate()
    {
        Cp6SloContractGuard.Utc(StartUtc, nameof(StartUtc));
        Cp6SloContractGuard.Utc(EndUtc, nameof(EndUtc));
        if (StartUtc >= EndUtc)
        {
            throw new ArgumentException("Evidence window start must precede its end.", nameof(StartUtc));
        }

        Cp6SloContractGuard.Coverage(ExpectedCoverage, nameof(ExpectedCoverage));
        Cp6SloContractGuard.Coverage(ObservedCoverage, nameof(ObservedCoverage));
    }
}

public sealed record Cp6SloMeasurement(
    [property: JsonPropertyName("sampleCount")] long SampleCount,
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("numerator")] decimal? Numerator,
    [property: JsonPropertyName("denominator")] decimal? Denominator,
    [property: JsonPropertyName("percentile")] decimal? Percentile,
    [property: JsonPropertyName("excludedCount")] long ExcludedCount)
{
    internal void Validate()
    {
        if (SampleCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(SampleCount), "Sample count cannot be negative.");
        }

        if (ExcludedCount < 0 || ExcludedCount > SampleCount)
        {
            throw new ArgumentOutOfRangeException(nameof(ExcludedCount), "Excluded count must be within the sample count.");
        }

        if (Numerator is < 0 || Denominator is <= 0 || Percentile is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(Numerator), "Measurement values are outside the supported range.");
        }
    }

    internal bool Meets(Cp6SloIndicator indicator) =>
        indicator.Comparator switch
        {
            Cp6SloComparator.GreaterThanOrEqual => Value >= indicator.Threshold,
            Cp6SloComparator.LessThanOrEqual => Value <= indicator.Threshold,
            _ => false
        };
}

public sealed record Cp6SloEvidenceSource(
    [property: JsonPropertyName("sourceType")] string SourceType,
    [property: JsonPropertyName("sourceId")] string SourceId,
    [property: JsonPropertyName("definitionDigest")] string DefinitionDigest,
    [property: JsonPropertyName("releaseArtifactDigest")] string ReleaseArtifactDigest,
    [property: JsonPropertyName("queryDefinitionDigest")] string QueryDefinitionDigest,
    [property: JsonPropertyName("evidenceArtifactDigest")] string EvidenceArtifactDigest,
    [property: JsonPropertyName("exclusionsVerified")] bool ExclusionsVerified)
{
    [JsonIgnore]
    public bool HasValidDigests =>
        Cp6SloContractGuard.IsDigest(DefinitionDigest) &&
        Cp6SloContractGuard.IsDigest(ReleaseArtifactDigest) &&
        Cp6SloContractGuard.IsDigest(QueryDefinitionDigest) &&
        Cp6SloContractGuard.IsDigest(EvidenceArtifactDigest);

    internal void Validate()
    {
        Cp6SloContractGuard.Identifier(SourceType, nameof(SourceType));
        Cp6SloContractGuard.Identifier(SourceId, nameof(SourceId));
    }
}

public sealed record Cp6SloEvidenceDocument(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("evidenceId")] string EvidenceId,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc,
    [property: JsonPropertyName("release")] Cp6ReleaseIdentity Release,
    [property: JsonPropertyName("sli")] Cp6SloIndicator Sli,
    [property: JsonPropertyName("window")] Cp6SloEvidenceWindow Window,
    [property: JsonPropertyName("measurement")] Cp6SloMeasurement Measurement,
    [property: JsonPropertyName("sources")] IReadOnlyList<Cp6SloEvidenceSource> Sources,
    [property: JsonPropertyName("completeness")] Cp6SloEvidenceCompleteness Completeness,
    [property: JsonPropertyName("result")] Cp6SloEvidenceResult Result,
    [property: JsonPropertyName("productionSloClaimed")] bool ProductionSloClaimed)
{
    public const string SchemaId = "https://contracts.cp6.uk/observability/slo-evidence/v1/schema.json";
    public const string CurrentSchemaVersion = "1.0.0";

    private const int MaximumJsonBytes = 1024 * 1024;

    private static JsonSerializerOptions SerializerOptions { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static Cp6SloEvidenceDocument Parse(ReadOnlyMemory<byte> json)
    {
        if (json.IsEmpty || json.Length > MaximumJsonBytes)
        {
            throw new JsonException("SLO evidence JSON is empty or exceeds the supported size.");
        }

        using var document = JsonDocument.Parse(
            json,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 32
            });
        EnsureNoDuplicateProperties(document.RootElement);
        var evidence = JsonSerializer.Deserialize<Cp6SloEvidenceDocument>(json.Span, SerializerOptions)
            ?? throw new JsonException("SLO evidence JSON is empty.");
        evidence.Validate();
        if (evidence.Result != Cp6SloEvidenceEvaluator.Evaluate(evidence))
        {
            throw new JsonException("SLO evidence result does not match the fail-closed evaluator.");
        }

        return evidence;
    }

    public void Validate()
    {
        if (!string.Equals(SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal))
        {
            throw new ArgumentException("SLO evidence schema version is not supported.", nameof(SchemaVersion));
        }

        Cp6SloContractGuard.Identifier(EvidenceId, nameof(EvidenceId));
        Cp6SloContractGuard.Utc(GeneratedAtUtc, nameof(GeneratedAtUtc));
        ArgumentNullException.ThrowIfNull(Release);
        ArgumentNullException.ThrowIfNull(Sli);
        ArgumentNullException.ThrowIfNull(Window);
        ArgumentNullException.ThrowIfNull(Measurement);
        ArgumentNullException.ThrowIfNull(Sources);
        Release.Validate();
        Sli.Validate();
        Window.Validate();
        Measurement.Validate();
        if (GeneratedAtUtc < Window.EndUtc)
        {
            throw new ArgumentException("Evidence cannot be generated before its measurement window ends.", nameof(GeneratedAtUtc));
        }

        foreach (var source in Sources)
        {
            ArgumentNullException.ThrowIfNull(source);
            source.Validate();
        }

        if (!Enum.IsDefined(Completeness))
        {
            throw new ArgumentOutOfRangeException(nameof(Completeness), "Evidence completeness is not supported.");
        }

        if (!Enum.IsDefined(Result))
        {
            throw new ArgumentOutOfRangeException(nameof(Result), "Evidence result is not supported.");
        }
    }

    private static void EnsureNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new JsonException("SLO evidence JSON contains a duplicate property.");
                }

                EnsureNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                EnsureNoDuplicateProperties(item);
            }
        }
    }
}

internal static partial class Cp6SloContractGuard
{
    internal static void Identifier(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > 128 ||
            !IdentifierPattern().IsMatch(value))
        {
            throw new ArgumentException("Value must be a bounded stable identifier.", parameterName);
        }
    }

    internal static void SemanticVersion(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || !SemanticVersionPattern().IsMatch(value))
        {
            throw new ArgumentException("Value must be a canonical three-part semantic version.", parameterName);
        }
    }

    internal static bool IsDigest(string? value) =>
        value is not null && DigestPattern().IsMatch(value);

    internal static void Utc(DateTimeOffset value, string parameterName)
    {
        if (value.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("Timestamp must use UTC offset Z.", parameterName);
        }
    }

    internal static void Coverage(decimal value, string parameterName)
    {
        if (value is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Coverage must be between zero and one.");
        }
    }

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex DigestPattern();
}
