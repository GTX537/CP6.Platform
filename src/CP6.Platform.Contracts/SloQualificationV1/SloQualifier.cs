using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Platform.Contracts.SloQualificationV1;

/// <summary>Owned bytes only; no path, URL, resolver, or network capability.</summary>
public sealed class FrozenArtifact
{
    private readonly byte[] bytes;
    public FrozenArtifact(ReadOnlySpan<byte> bytes, string expectedSha256)
    {
        if (bytes.Length is 0 or > 4_194_304) throw new ArgumentOutOfRangeException(nameof(bytes));
        this.bytes = bytes.ToArray();
        Sha256 = Convert.ToHexString(SHA256.HashData(this.bytes)).ToLowerInvariant();
        if (!string.Equals(Sha256, expectedSha256, StringComparison.Ordinal)) throw new ArgumentException("Supplied artifact bytes do not match pinned SHA-256.");
    }
    public string Sha256 { get; }
    public int ByteLength => bytes.Length;
    public byte[] CopyBytes() => (byte[])bytes.Clone();
    internal T Read<T>()
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 32 });
        RejectDuplicates(document.RootElement);
        RequireFields(document.RootElement, typeof(T));
        return JsonSerializer.Deserialize<T>(bytes, JsonOptions) ?? throw new JsonException("Missing document.");
    }
    private static void RequireFields(JsonElement value, Type type)
    {
        if (value.ValueKind == JsonValueKind.Null) return; // Nullable content is validated by its method-specific rule.
        if (type.IsArray)
        {
            if (value.ValueKind != JsonValueKind.Array) throw new JsonException("Expected evidence array.");
            foreach (var item in value.EnumerateArray()) RequireFields(item, type.GetElementType()!);
        }
        else if (type.Namespace == typeof(SloDefinition).Namespace && !type.IsEnum)
        {
            if (value.ValueKind != JsonValueKind.Object) throw new JsonException("Expected evidence object.");
            foreach (var property in type.GetProperties())
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                if (!value.TryGetProperty(name, out var child)) throw new JsonException("Required evidence field is missing.");
                RequireFields(child, property.PropertyType);
            }
        }
    }
    private static void RejectDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException("Duplicate evidence property.");
                RejectDuplicates(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) RejectDuplicates(item);
    }
    public static JsonSerializerOptions JsonOptions => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 32
    };
}

public enum SloMethod { ExactRatio, WeightedNearestRank, GaugeMaximum }
public enum ThresholdDirection { AtLeast, AtMost }
public enum QualificationState { Qualified, Indeterminate }
public sealed record SloDefinition(string SchemaVersion, string DefinitionVersion, SloMethod Method,
    string Unit, string QuerySha256, string ExclusionRule, string Threshold, ThresholdDirection Direction,
    string? Percentile = null, long? SamplingIntervalTicks = null, long? MaximumGapTicks = null);
public sealed record QueryBinding(string SchemaVersion, string Expression, string PopulationName, string Unit,
    string Denominator, string ExclusionRule);
public sealed record PopulationMember(string Key, string ReleaseDigest, DateTimeOffset Timestamp, bool Excluded, string Weight);
public sealed record PopulationInventory(string SchemaVersion, string PopulationName, string AuthorityReference,
    DateTimeOffset WindowStart, DateTimeOffset WindowEnd, PopulationMember[] Members);
public sealed record SourceObservation(string Key, string ReleaseDigest, DateTimeOffset Timestamp, string Unit,
    bool Excluded, string Value, string Weight);
public sealed record ObservationSource(string SchemaVersion, string DefinitionSha256, string QuerySha256,
    string InventorySha256, DateTimeOffset WindowStart, DateTimeOffset WindowEnd, DateTimeOffset SealedAt,
    string Completeness, long DroppedCount, bool Overflowed, SourceObservation[] Observations);
/// <summary>Trusted caller binds inventory independently of the sample export. This binding is not producer authentication.</summary>
public sealed record QualificationBinding(string InventorySha256, string InventoryAuthorityReference,
    string DefinitionSha256, string QuerySha256, string ReleaseDigest, DateTimeOffset WindowStart, DateTimeOffset WindowEnd);
public sealed record QualificationInput(FrozenArtifact Definition, FrozenArtifact Query, FrozenArtifact Inventory,
    FrozenArtifact Source, QualificationBinding Binding);
public sealed record QualificationResult(QualificationState State, Cp6SloEvidenceResult CandidateThresholdResult,
    ExactNumber? RecomputedValue, string Reason)
{
    public string SchemaVersion => "cp6.slo-qualification/1.0.0";
    public bool ProductionSloClaimed => false;
    public bool IndependentlyAccepted => false;
}

/// <summary>Explicit pre-evaluation gate; does not change or silently invoke the legacy 1.0.0 evaluator.</summary>
public static class SloQualifier
{
    public static QualificationResult Qualify(QualificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        try { return Compute(input); }
        catch (Exception ex) when (ex is JsonException or ArgumentException or FormatException or OverflowException or DivideByZeroException or InvalidOperationException or NullReferenceException)
        { return Indeterminate("Invalid or unsupported evidence input."); }
    }
    private static QualificationResult Compute(QualificationInput input)
    {
        var d = input.Definition.Read<SloDefinition>();
        var q = input.Query.Read<QueryBinding>();
        var p = input.Inventory.Read<PopulationInventory>();
        var s = input.Source.Read<ObservationSource>();
        var b = input.Binding;
        if (d.SchemaVersion != "1.0.0" || q.SchemaVersion != "1.0.0" || p.SchemaVersion != "1.0.0" || s.SchemaVersion != "1.0.0" ||
            string.IsNullOrWhiteSpace(d.DefinitionVersion) || string.IsNullOrWhiteSpace(q.Expression) || string.IsNullOrWhiteSpace(d.Unit) ||
            string.IsNullOrWhiteSpace(p.AuthorityReference) || string.IsNullOrWhiteSpace(p.PopulationName) || !IsDigest(b.ReleaseDigest) ||
            b.InventorySha256 != input.Inventory.Sha256 || b.InventoryAuthorityReference != p.AuthorityReference ||
            b.DefinitionSha256 != input.Definition.Sha256 || b.QuerySha256 != input.Query.Sha256 ||
            d.QuerySha256 != input.Query.Sha256 || s.QuerySha256 != input.Query.Sha256 ||
            s.DefinitionSha256 != input.Definition.Sha256 || s.InventorySha256 != input.Inventory.Sha256 ||
            q.Unit != d.Unit || q.ExclusionRule != d.ExclusionRule || q.PopulationName != p.PopulationName ||
            q.Denominator != "included-inventory-weights" || d.ExclusionRule is not ("none" or "inventory-declared") || !Enum.IsDefined(d.Method) || !Enum.IsDefined(d.Direction))
            return Indeterminate("Definition/query/inventory/release binding mismatch.");
        if (b.WindowStart.Offset != TimeSpan.Zero || b.WindowEnd.Offset != TimeSpan.Zero || b.WindowStart >= b.WindowEnd ||
            p.WindowStart.Offset != TimeSpan.Zero || p.WindowEnd.Offset != TimeSpan.Zero || s.WindowStart.Offset != TimeSpan.Zero || s.WindowEnd.Offset != TimeSpan.Zero ||
            p.WindowStart != b.WindowStart || p.WindowEnd != b.WindowEnd || s.WindowStart != b.WindowStart || s.WindowEnd != b.WindowEnd ||
            s.SealedAt.Offset != TimeSpan.Zero || s.SealedAt < s.WindowEnd || s.Completeness != "Complete" || s.DroppedCount != 0 || s.Overflowed)
            return Indeterminate("Window is not closed or evidence is incomplete.");
        if (p.Members.Length is 0 or > 10000 || s.Observations.Length != p.Members.Length)
            return Indeterminate("Independent population coverage is missing.");
        var population = p.Members.ToDictionary(x => x.Key, StringComparer.Ordinal);
        var observations = s.Observations.ToDictionary(x => x.Key, StringComparer.Ordinal);
        if (population.Count != observations.Count || population.Keys.Any(k => !observations.ContainsKey(k)))
            return Indeterminate("Population membership mismatch.");
        var included = new List<(SourceObservation Row, ExactNumber Value, BigInteger Weight)>();
        foreach (var (key, member) in population)
        {
            var row = observations[key];
            if (string.IsNullOrWhiteSpace(key) || row.ReleaseDigest != b.ReleaseDigest || member.ReleaseDigest != b.ReleaseDigest ||
                row.Timestamp.Offset != TimeSpan.Zero || member.Timestamp.Offset != TimeSpan.Zero || row.Timestamp != member.Timestamp || row.Timestamp < b.WindowStart || row.Timestamp >= b.WindowEnd ||
                row.Excluded != member.Excluded || row.Unit != d.Unit || row.Weight != member.Weight || (row.Excluded && d.ExclusionRule == "none"))
                return Indeterminate("Observation identity, timestamp, exclusions, unit or release mismatch.");
            var weight = ExactNumber.Parse(row.Weight);
            if (weight.Denominator != BigInteger.One || weight.Numerator <= 0)
                return Indeterminate("Weights must be explicit positive integers.");
            var value = ExactNumber.Parse(row.Value);
            if (!row.Excluded) included.Add((row, value, weight.Numerator));
        }
        if (included.Count == 0) return Indeterminate("Zero included samples.");
        ExactNumber measured;
        if (d.Method == SloMethod.ExactRatio)
        {
            if (d.Unit is not ("ratio" or "percent")) return Indeterminate("Unsupported ratio unit.");
            if (included.Any(x => x.Value.Denominator != BigInteger.One || x.Value.Numerator < 0 || x.Value.Numerator > 1))
                return Indeterminate("Ratio requires an exact 0/1 indicator per population member.");
            var n = included.Aggregate(BigInteger.Zero, (sum, x) => sum + x.Weight);
            var k = included.Aggregate(BigInteger.Zero, (sum, x) => sum + x.Value.Numerator * x.Weight);
            measured = ExactNumber.FromRational(d.Unit == "percent" ? k * 100 : k, n);
        }
        else if (d.Method == SloMethod.WeightedNearestRank)
        {
            var percentile = ExactNumber.Parse(d.Percentile ?? "");
            if (percentile.Numerator <= 0 || percentile.CompareTo(ExactNumber.Parse("100")) > 0)
                return Indeterminate("Percentile must be within (0,100].");
            var sorted = included.OrderBy(x => x.Value).ThenBy(x => x.Row.Key, StringComparer.Ordinal).ToArray();
            var total = sorted.Aggregate(BigInteger.Zero, (sum, x) => sum + x.Weight);
            var rankNumerator = percentile.Numerator * total;
            var rankDenominator = percentile.Denominator * 100;
            var rank = (rankNumerator + rankDenominator - 1) / rankDenominator;
            var cumulative = BigInteger.Zero;
            measured = sorted[^1].Value;
            foreach (var row in sorted)
            {
                cumulative += row.Weight;
                if (cumulative >= rank) { measured = row.Value; break; }
            }
        }
        else
        {
            if (d.SamplingIntervalTicks is null or <= 0 || d.MaximumGapTicks is null or < 0 || included.Any(x => x.Weight != BigInteger.One))
                return Indeterminate("Gauge sampling interval and maximum gaps must be declared with unit weights.");
            var ordered = included.OrderBy(x => x.Row.Timestamp).ToArray();
            var expected = b.WindowStart;
            var previous = b.WindowStart;
            foreach (var item in ordered)
            {
                if (item.Row.Timestamp != expected || (item.Row.Timestamp - previous).Ticks > d.MaximumGapTicks)
                    return Indeterminate("Gauge sample missing or gap exceeds declared limit.");
                previous = item.Row.Timestamp;
                expected = expected.AddTicks(d.SamplingIntervalTicks.Value);
            }
            if (expected < b.WindowEnd || (b.WindowEnd - previous).Ticks > d.MaximumGapTicks)
                return Indeterminate("Gauge tail gap or sample missing.");
            measured = ordered.MaxBy(x => x.Value).Value;
        }
        var comparison = measured.CompareTo(ExactNumber.Parse(d.Threshold));
        var meets = d.Direction == ThresholdDirection.AtLeast ? comparison >= 0 : comparison <= 0;
        return new(QualificationState.Qualified, meets ? Cp6SloEvidenceResult.Pass : Cp6SloEvidenceResult.Fail, measured, "Local recomputation only; external authenticity and independent acceptance unproven.");
    }
    private static bool IsDigest(string value) => value?.Length == 64 && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static QualificationResult Indeterminate(string reason) => new(QualificationState.Indeterminate, Cp6SloEvidenceResult.Indeterminate, null, reason);
}

/// <summary>Append-only, bounded local journal. A failure is retained; corrections use a new ID and increasing version.</summary>
public sealed class QualificationJournal
{
    private readonly object gate = new();
    private readonly Dictionary<string, QualificationEntry> entries = new(StringComparer.Ordinal);
    private long storedBytes;
    public QualificationEntry Append(string evidenceId, int version, string? supersedes, QualificationInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceId);
        ArgumentNullException.ThrowIfNull(input);
        lock (gate)
        {
            var size = (long)input.Source.ByteLength + input.Definition.ByteLength + input.Query.ByteLength + input.Inventory.ByteLength;
            if (evidenceId.Length > 256 || entries.Count >= 1000 || size > 67_108_864 - storedBytes || entries.ContainsKey(evidenceId)) throw new InvalidOperationException("Journal full or immutable evidence identity already exists.");
            if (version < 1 || (supersedes is null && version != 1) ||
                (supersedes is not null && (!entries.TryGetValue(supersedes, out var previous) || previous.Version + 1 != version ||
                    previous.Binding.WindowStart != input.Binding.WindowStart || previous.Binding.WindowEnd != input.Binding.WindowEnd ||
                    previous.Binding.ReleaseDigest != input.Binding.ReleaseDigest || previous.Binding.InventoryAuthorityReference != input.Binding.InventoryAuthorityReference)))
                throw new InvalidOperationException("Revision must reference prior evidence in the same window/release/authority lineage.");
            var entry = new QualificationEntry(evidenceId, version, supersedes, input.Binding, input.Source, input.Definition, input.Query, input.Inventory, SloQualifier.Qualify(input));
            entries.Add(evidenceId, entry);
            storedBytes += size;
            return entry;
        }
    }
    public IReadOnlyList<QualificationEntry> Entries { get { lock (gate) return Array.AsReadOnly(entries.Values.ToArray()); } }
}
public sealed record QualificationEntry(string EvidenceId, int Version, string? Supersedes,
    QualificationBinding Binding, FrozenArtifact Source, FrozenArtifact Definition, FrozenArtifact Query, FrozenArtifact Inventory, QualificationResult Result);
