namespace CP6.Platform.Contracts;

/// <summary>
/// Computes a fail-closed SLO evidence result from validated typed evidence.
/// </summary>
public static class Cp6SloEvidenceEvaluator
{
    public static Cp6SloEvidenceResult Evaluate(Cp6SloEvidenceDocument evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        evidence.Validate();

        if (!evidence.Release.Candidate ||
            evidence.Completeness != Cp6SloEvidenceCompleteness.Complete ||
            evidence.Window.ExpectedCoverage != 1m ||
            evidence.Window.ObservedCoverage != evidence.Window.ExpectedCoverage ||
            evidence.Measurement.SampleCount == 0 ||
            evidence.Sources.Count == 0 ||
            !evidence.Sli.HasValidDefinitionDigest ||
            evidence.Sources.Any(source =>
                !source.HasValidDigests ||
                !string.Equals(
                    source.ReleaseArtifactDigest,
                    evidence.Release.ArtifactDigest,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    source.DefinitionDigest,
                    evidence.Sli.DefinitionDigest,
                    StringComparison.Ordinal)) ||
            (evidence.Measurement.ExcludedCount > 0 &&
                evidence.Sources.Any(source => !source.ExclusionsVerified)))
        {
            return Cp6SloEvidenceResult.Indeterminate;
        }

        return evidence.Measurement.Meets(evidence.Sli)
            ? Cp6SloEvidenceResult.Pass
            : Cp6SloEvidenceResult.Fail;
    }
}
