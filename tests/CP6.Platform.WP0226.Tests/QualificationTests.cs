using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Platform.Contracts;
using CP6.Platform.Contracts.SloQualificationV1;
using CP6.Platform.Testing.EvidenceV1;

namespace CP6.Platform.WP0226.Tests;

public class QualificationTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
    private static readonly string Release = new('a', 64);
    internal static FrozenArtifact Freeze<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, FrozenArtifact.JsonOptions);
        return new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
    private static T Read<T>(FrozenArtifact artifact) => JsonSerializer.Deserialize<T>(artifact.CopyBytes(), FrozenArtifact.JsonOptions)!;
    private static QualificationInput Fixture(SloMethod method = SloMethod.ExactRatio, string exclusionRule = "none", int excludedIndex = -1)
    {
        var unit = method == SloMethod.ExactRatio ? "ratio" : "ms";
        var query = Freeze(new QueryBinding("1.0.0", "fixture exported indicators; no database execution", "fixture-inventory", unit, "included-inventory-weights", exclusionRule));
        var definition = Freeze(new SloDefinition("1.0.0", "fixture-1", method, unit, query.Sha256, exclusionRule, method == SloMethod.ExactRatio ? "0.6" : "20",
            method == SloMethod.ExactRatio ? ThresholdDirection.AtLeast : ThresholdDirection.AtMost, "95", TimeSpan.TicksPerSecond, TimeSpan.TicksPerSecond));
        var members = Enumerable.Range(0, 3).Select(i => new PopulationMember($"sample-{i}", Release, Start.AddSeconds(i), i == excludedIndex, method == SloMethod.WeightedNearestRank && i == 0 ? "99" : "1")).ToArray();
        var inventory = Freeze(new PopulationInventory("1.0.0", "fixture-inventory", "independent-test-inventory-v1", Start, Start.AddSeconds(3), members));
        var rows = members.Select((m, i) => new SourceObservation(m.Key, Release, m.Timestamp, unit, m.Excluded,
            method == SloMethod.ExactRatio ? (i < 2 ? "1" : "0") : (10 + i * 10).ToString(), m.Weight)).ToArray();
        var source = Freeze(new ObservationSource("1.0.0", definition.Sha256, query.Sha256, inventory.Sha256, Start, Start.AddSeconds(3), Start.AddSeconds(4), "Complete", 0, false, rows));
        return new(definition, query, inventory, source, new(inventory.Sha256, "independent-test-inventory-v1", definition.Sha256, query.Sha256, Release, Start, Start.AddSeconds(3)));
    }
    [Fact]
    public void Same_frozen_bytes_recompute_exact_K_over_N_and_remain_nonproduction()
    {
        var input = Fixture();
        var result = SloQualifier.Qualify(input);
        Assert.Equal(QualificationState.Qualified, result.State);
        Assert.Equal(Cp6SloEvidenceResult.Pass, result.CandidateThresholdResult);
        ExactAssert.Equal(ExactNumber.FromRational(2, 3), result.RecomputedValue!);
        Assert.Equal(result.RecomputedValue!.ToString(), SloQualifier.Qualify(input).RecomputedValue!.ToString());
        Assert.False(result.ProductionSloClaimed);
        Assert.False(result.IndependentlyAccepted);
        var copy = input.Source.CopyBytes(); copy[0] ^= 1;
        Assert.Throws<ArgumentException>(() => new FrozenArtifact(copy, input.Source.Sha256));
        Assert.Equal(Cp6SloEvidenceResult.Pass, SloQualifier.Qualify(input).CandidateThresholdResult);
    }
    [Fact]
    public void Weighted_nearest_rank_is_not_average_of_percentiles()
    {
        var result = SloQualifier.Qualify(Fixture(SloMethod.WeightedNearestRank));
        Assert.Equal(QualificationState.Qualified, result.State);
        ExactAssert.Equal(ExactNumber.Parse("10"), result.RecomputedValue!);
    }
    [Fact]
    public void Gauge_maximum_uses_bound_sampling_grid_and_tail_gap()
    {
        var result = SloQualifier.Qualify(Fixture(SloMethod.GaugeMaximum));
        Assert.Equal(Cp6SloEvidenceResult.Fail, result.CandidateThresholdResult);
        ExactAssert.Equal(ExactNumber.Parse("30"), result.RecomputedValue!);
    }
    [Theory]
    [InlineData("missing")][InlineData("zero")][InlineData("dropped")][InlineData("overflow")][InlineData("incomplete")]
    [InlineData("unit")][InlineData("release")][InlineData("denominator")][InlineData("query")][InlineData("excluded")]
    [InlineData("duplicate")][InlineData("window")][InlineData("unsealed")][InlineData("fractional-weight")]
    public void Bad_evidence_cannot_qualify(string mutation)
    {
        var input = Fixture();
        var source = Read<ObservationSource>(input.Source);
        var rows = source.Observations.ToArray();
        source = mutation switch
        {
            "missing" => source with { Observations = rows[..2] },
            "zero" => source with { Observations = [] },
            "dropped" => source with { DroppedCount = 1 },
            "overflow" => source with { Overflowed = true },
            "incomplete" => source with { Completeness = "Incomplete" },
            "query" => source with { QuerySha256 = new('b', 64) },
            "window" => source with { WindowEnd = Start.AddSeconds(9) },
            "unsealed" => source with { SealedAt = Start },
            _ => source
        };
        if (mutation == "unit") rows[0] = rows[0] with { Unit = "percent" };
        if (mutation == "release") rows[0] = rows[0] with { ReleaseDigest = new('b', 64) };
        if (mutation == "denominator") rows[0] = rows[0] with { Weight = "9999" };
        if (mutation == "fractional-weight") rows[0] = rows[0] with { Weight = "0.5" };
        if (mutation == "excluded") rows[0] = rows[0] with { Excluded = true };
        if (mutation == "duplicate") rows[1] = rows[0];
        if (mutation is "unit" or "release" or "denominator" or "fractional-weight" or "excluded" or "duplicate") source = source with { Observations = rows };
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input with { Source = Freeze(source) }).State);
    }
    [Fact]
    public void Forged_self_count_inventory_and_query_drift_do_not_replace_independent_binding()
    {
        var input = Fixture();
        var forged = Freeze(Read<PopulationInventory>(input.Inventory) with { Members = Read<PopulationInventory>(input.Inventory).Members[..1] });
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input with { Inventory = forged }).State);
        var changedQuery = Freeze(Read<QueryBinding>(input.Query) with { Denominator = "received-samples" });
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input with { Query = changedQuery }).State);
    }
    [Fact]
    public void Journal_retains_original_failure_and_refuses_late_overwrite()
    {
        var input = Fixture();
        var bad = input with { Source = Freeze(Read<ObservationSource>(input.Source) with { Completeness = "Incomplete" }) };
        var journal = new QualificationJournal();
        var failed = journal.Append("attempt-1", 1, null, bad);
        Assert.Throws<InvalidOperationException>(() => journal.Append("attempt-1", 1, null, input));
        var corrected = journal.Append("attempt-2", 2, "attempt-1", input);
        Assert.Equal(QualificationState.Indeterminate, failed.Result.State);
        Assert.Equal(QualificationState.Qualified, corrected.Result.State);
        Assert.Equal(2, journal.Entries.Count);
        Assert.Equal(bad.Source.Sha256, journal.Entries[0].Source.Sha256);
    }
    [Fact]
    public void Duplicate_json_fields_are_rejected_even_with_valid_digest()
    {
        var input = Fixture();
        var raw = Encoding.UTF8.GetString(input.Query.CopyBytes()).Replace("\"schemaVersion\":\"1.0.0\"", "\"schemaVersion\":\"1.0.0\",\"schemaVersion\":\"1.0.0\"", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(raw);
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input with { Query = new(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()) }).State);
    }

    [Fact]
    public void Missing_required_false_or_zero_field_cannot_be_defaulted_into_completeness()
    {
        var input = Fixture();
        var raw = Encoding.UTF8.GetString(input.Source.CopyBytes()).Replace("\"overflowed\":false,", "", StringComparison.Ordinal);
        var bytes = Encoding.UTF8.GetBytes(raw);
        var source = new FrozenArtifact(bytes, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input with { Source = source }).State);
    }

    [Fact]
    public void Gauge_sampling_gap_is_rejected_even_when_member_count_matches()
    {
        var input = Fixture(SloMethod.GaugeMaximum);
        var inventory = Read<PopulationInventory>(input.Inventory);
        var members = inventory.Members.ToArray();
        members[1] = members[1] with { Timestamp = Start.AddMilliseconds(1500) };
        var changedInventory = Freeze(inventory with { Members = members });
        var source = Read<ObservationSource>(input.Source);
        var rows = source.Observations.ToArray(); rows[1] = rows[1] with { Timestamp = members[1].Timestamp };
        var changed = input with { Inventory = changedInventory, Binding = input.Binding with { InventorySha256 = changedInventory.Sha256 }, Source = Freeze(source with { InventorySha256 = changedInventory.Sha256, Observations = rows }) };
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(changed).State);
    }

    [Fact]
    public void Unknown_exclusion_algorithm_cannot_qualify_from_matching_strings()
    {
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(Fixture(exclusionRule: "unimplemented-expression")).State);
    }

    [Fact]
    public void Explicit_inventory_exclusion_is_bound_separately_from_received_samples()
    {
        var result = SloQualifier.Qualify(Fixture(exclusionRule: "inventory-declared", excludedIndex: 2));
        Assert.Equal(QualificationState.Qualified, result.State);
        ExactAssert.Equal(ExactNumber.Parse("1"), result.RecomputedValue!);
    }

    [Fact]
    public void Corrected_query_and_definition_use_new_superseding_identity_and_retain_original_bytes()
    {
        var original = Fixture();
        var query = Freeze(Read<QueryBinding>(original.Query) with { Expression = "corrected immutable query" });
        var definition = Freeze(Read<SloDefinition>(original.Definition) with { QuerySha256 = query.Sha256, DefinitionVersion = "fixture-2" });
        var revised = original with
        {
            Query = query, Definition = definition,
            Binding = original.Binding with { QuerySha256 = query.Sha256, DefinitionSha256 = definition.Sha256 },
            Source = Freeze(Read<ObservationSource>(original.Source) with { QuerySha256 = query.Sha256, DefinitionSha256 = definition.Sha256 })
        };
        var journal = new QualificationJournal();
        journal.Append("original", 1, null, original);
        Assert.Equal(QualificationState.Qualified, journal.Append("corrected", 2, "original", revised).Result.State);
        Assert.Equal(original.Query.Sha256, journal.Entries[0].Query.Sha256);
        Assert.NotEqual(original.Query.Sha256, journal.Entries[1].Query.Sha256);
        Assert.Throws<InvalidOperationException>(() => journal.Append("wrong-window", 3, "corrected", revised with { Binding = revised.Binding with { WindowEnd = Start.AddHours(1) } }));
    }

    [Theory]
    [InlineData("inventory-window")][InlineData("source-window")][InlineData("member")]
    public void UTC_offsets_are_verified_on_each_supplied_artifact(string target)
    {
        var input = Fixture();
        if (target == "source-window")
            input = input with { Source = Freeze(Read<ObservationSource>(input.Source) with { WindowStart = Start.ToOffset(TimeSpan.FromHours(1)) }) };
        else
        {
            var inventory = Read<PopulationInventory>(input.Inventory);
            if (target == "inventory-window") inventory = inventory with { WindowStart = Start.ToOffset(TimeSpan.FromHours(1)) };
            else inventory.Members[0] = inventory.Members[0] with { Timestamp = Start.ToOffset(TimeSpan.FromHours(1)) };
            var frozen = Freeze(inventory);
            input = input with { Inventory = frozen, Binding = input.Binding with { InventorySha256 = frozen.Sha256 }, Source = Freeze(Read<ObservationSource>(input.Source) with { InventorySha256 = frozen.Sha256 }) };
        }
        Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(input).State);
    }

    [Fact]
    public void Zero_included_population_and_zero_bound_weight_are_indeterminate()
    {
        var input = Fixture(exclusionRule: "inventory-declared");
        foreach (var zeroWeight in new[] { false, true })
        {
            var population = Read<PopulationInventory>(input.Inventory);
            var members = population.Members.Select(m => zeroWeight ? m with { Weight = "0" } : m with { Excluded = true }).ToArray();
            var inventory = Freeze(population with { Members = members });
            var source = Read<ObservationSource>(input.Source);
            var rows = source.Observations.Select(r => zeroWeight ? r with { Weight = "0" } : r with { Excluded = true }).ToArray();
            var candidate = input with { Inventory = inventory, Binding = input.Binding with { InventorySha256 = inventory.Sha256 }, Source = Freeze(source with { InventorySha256 = inventory.Sha256, Observations = rows }) };
            Assert.Equal(QualificationState.Indeterminate, SloQualifier.Qualify(candidate).State);
        }
    }

    [Fact]
    public void Percent_unit_is_exactly_one_hundred_times_ratio()
    {
        var input = Fixture();
        var query = Freeze(Read<QueryBinding>(input.Query) with { Unit = "percent" });
        var definition = Freeze(Read<SloDefinition>(input.Definition) with { Unit = "percent", QuerySha256 = query.Sha256, Threshold = "60" });
        var source = Read<ObservationSource>(input.Source);
        var candidate = input with { Query = query, Definition = definition, Binding = input.Binding with { QuerySha256 = query.Sha256, DefinitionSha256 = definition.Sha256 },
            Source = Freeze(source with { QuerySha256 = query.Sha256, DefinitionSha256 = definition.Sha256, Observations = source.Observations.Select(r => r with { Unit = "percent" }).ToArray() }) };
        var result = SloQualifier.Qualify(candidate);
        Assert.Equal(Cp6SloEvidenceResult.Pass, result.CandidateThresholdResult);
        ExactAssert.Equal(ExactNumber.FromRational(200, 3), result.RecomputedValue!);
    }
}
