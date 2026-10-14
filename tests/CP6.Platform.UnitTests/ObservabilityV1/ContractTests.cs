using CP6.Platform.Contracts;
using CP6.Platform.Contracts.ObservabilityV1;

namespace CP6.Platform.UnitTests.ObservabilityV1;

public sealed class ContractTests
{
    [Fact]
    public void BindingRejectsNonCanonicalAndNonUtcValues()
    {
        Assert.Throws<ArgumentException>(() => Binding(environment: "Prod"));
        Assert.Throws<ArgumentException>(() => Binding(started: DateTimeOffset.Now.ToOffset(TimeSpan.FromHours(1))));
        Assert.Equal("1.0.0", Binding().ContractVersion);
    }

    [Fact]
    public void PolicyCopiesFiniteRegistriesAndRejectsArbitraryValuesUnderAllowedKeys()
    {
        var routes = new[] { "/orders/{id}" };
        var policy = new TelemetryPolicyV1("orders", "test", "east", routes);
        routes[0] = "/secret";
        Assert.True(policy.Allows("http.route", "/orders/{id}"));
        Assert.False(policy.Allows("http.route", "/secret"));
        Assert.False(policy.Allows("cp6.outcome", "customer-123"));
        Assert.False(policy.Allows("HTTP.ROUTE", "/orders/{id}"));
        Assert.Throws<ArgumentException>(() => new TelemetryPolicyV1("orders", "test", "east", ["/orders?password=x"]));
    }

    [Fact]
    public void ObservationNeverPromotesCandidateOrClaimsProduction()
    {
        var result = new ObservationEnvelopeV1(IdentityVerdictV1.Match, TelemetryCompletenessV1.Incomplete);
        Assert.False(result.ProductionAccepted);
        Assert.Equal(EvidenceStatusV1.Unproven, result.CurrentPackageTrust);
        Assert.Equal(EvidenceStatusV1.NotEvaluated, result.IndependentQualification);
        Assert.Equal("local-nonproduction", result.Scope);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationEnvelopeV1((IdentityVerdictV1)999, TelemetryCompletenessV1.Incomplete));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationEnvelopeV1(IdentityVerdictV1.Match, (TelemetryCompletenessV1)999));
    }

    [Fact]
    public void CandidateFlagsAndValidLookingPackageLockCannotPromoteLocalEnvelope()
    {
        const string payload = "{\"RuntimeObservation\":1,\"Telemetry\":0,\"CandidatePass\":true,\"ProductionSloClaimed\":true,\"ProductionAccepted\":true,\"CurrentPackageTrust\":1,\"IndependentQualification\":1,\"SourceHash\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\",\"ValidSevenPackageLock\":true,\"MatchingDi\":true}";
        var envelope = System.Text.Json.JsonSerializer.Deserialize<ObservationEnvelopeV1>(payload)!;
        Assert.False(envelope.ProductionAccepted);
        Assert.Equal(EvidenceStatusV1.Unproven, envelope.CurrentPackageTrust);
        Assert.Equal(EvidenceStatusV1.NotEvaluated, envelope.IndependentQualification);
        Assert.Equal("local-nonproduction", envelope.Scope);
    }

    [Theory]
    [InlineData("1 invalid")]
    [InlineData("SECRET")]
    [InlineData("a/../b")]
    public void BindingRejectsUnsafeLabelsWithoutEcho(string input)
    {
        var error = Assert.Throws<ArgumentException>(() => Binding(environment: input));
        Assert.DoesNotContain(input, error.Message);
    }

    [Fact]
    public void ContractPreservesTechnicalMetricSemanticsAndOwnerLatencyHasSeparateBusinessMeaning()
    {
        Assert.Equal("transport-return", MetricDefinitionV1.Technical.Single(d => d.Name == "cp6.messaging.published").MeasurementPoint);
        Assert.Equal("delivery-validation", MetricDefinitionV1.Technical.Single(d => d.Name == "cp6.messaging.consumed").MeasurementPoint);
        Assert.All(MetricDefinitionV1.Technical, d => Assert.Equal("attempt", d.Scope));
        Assert.Equal("business", MetricDefinitionV1.OwnerLatency.Scope);
        Assert.Equal(MetricCommitPhaseV1.AfterCommit, MetricDefinitionV1.OwnerLatency.CommitPhase);
        Assert.Equal("s", MetricDefinitionV1.OwnerLatency.Unit);
    }

    [Theory]
    [InlineData("cp6.messaging.rejected")]
    [InlineData("cp6.messaging.trace_context.rejected")]
    [InlineData("cp6.messaging.duration")]
    [InlineData("cp6.outbox.oldest_available.age")]
    [InlineData("cp6.messaging.attempts")]
    public void ExistingTechnicalMetricsHaveVersionedDefinitions(string name) => Assert.Contains(MetricDefinitionV1.Technical, d => d.Name == name);

    [Fact]
    public void OwnerLatencyRequiresDistinctCommittedReceiptsAndClockProvenance()
    {
        Assert.Equal(LatencyStatusV1.NotMeasured, OwnerReceiptLatencyV1.Measure(null, null).Status);
        var now = DateTimeOffset.UtcNow;
        var producer = new OwnerReceiptV1("message-a", "producer-a", ReceiptPointV1.ProducerCommitted, now, "clock-a", false);
        var consumer = new OwnerReceiptV1("message-a", "consumer-a", ReceiptPointV1.ConsumerApplied, now.AddSeconds(2), "clock-a", false);
        Assert.Equal(2, OwnerReceiptLatencyV1.Measure(producer, consumer).Seconds);
        Assert.Equal(LatencyStatusV1.NotMeasured, OwnerReceiptLatencyV1.Measure(producer, consumer with { IsRetryOrDuplicate = true }).Status);
        Assert.Equal(LatencyStatusV1.ClockUnproven, OwnerReceiptLatencyV1.Measure(producer, consumer with { ClockProvenance = "clock-b" }).Status);
        Assert.Equal(LatencyStatusV1.ClockSkew, OwnerReceiptLatencyV1.Measure(producer, consumer with { AtUtc = now.AddSeconds(-1) }).Status);
        Assert.Equal(MetricCommitPhaseV1.BeforeCommit, MetricDefinitionV1.Technical.Single(x => x.Name == "cp6.outbox.operations").CommitPhase);
    }

    private static RuntimeIdentityBindingV1 Binding(string environment = "test", DateTimeOffset? started = null) => new(
        new Cp6ReleaseIdentity("orders", "1.0.0", new string('a', 40), "sha256:" + new string('b', 64), "sha256:" + new string('c', 64), Cp6ReleaseMode.Candidate),
        environment, "east", "packages-a", "sha256:" + new string('d', 64), "schemas-a", "deploy-a", "process-a", 1,
        started ?? DateTimeOffset.UtcNow.AddSeconds(-1), TimeSpan.FromMinutes(1));
}
