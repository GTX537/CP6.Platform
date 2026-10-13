namespace CP6.Platform.Release.CapabilitiesV1;

public enum Cp6CapabilityState { Unknown, Verified }
public enum Cp6CompatibilityDecision { Unknown, Blocked, Allowed }

/// <summary>Trusted caller configuration, never derived solely from the document being evaluated.</summary>
public sealed record Cp6CapabilityApproval(string ManifestSha256, string SourceGitSha, string? CurrentTrustPolicySha256 = null);

/// <summary>Host-owned cryptographic verifier. Implement using the existing formal package CLI;
/// never implement by reading success claims from publication JSON. The evaluator independently
/// validates the publication, policy, current signer, and all byte identities before invoking this boundary.</summary>
public interface ICp6CapabilitySignatureVerifier
{
    bool VerifyCurrent(ReadOnlyMemory<byte> publication, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> packages,
        Cp6PinnedNuGetTrustPolicy policy, DateTimeOffset evaluationUtc);
}

public sealed class Cp6CapabilityEvaluation(
    Cp6CapabilityState SourceFrozen, Cp6CapabilityState ContractShapeValid,
    Cp6CapabilityState ArtifactBytesVerified, Cp6CapabilityState TrustCurrentlyAccepted,
    Cp6CompatibilityDecision Compatibility, string PublicationStatus, string TrustStatus,
    byte[] consumerLock,
    Cp6CapabilityState ConsumerQualified = Cp6CapabilityState.Unknown,
    Cp6CapabilityState HostRegistered = Cp6CapabilityState.Unknown,
    Cp6CapabilityState ConfigEnabled = Cp6CapabilityState.Unknown,
    Cp6CapabilityState RuntimeObserved = Cp6CapabilityState.Unknown,
    Cp6CapabilityState BusinessApplied = Cp6CapabilityState.Unknown,
    Cp6CapabilityState IndependentlyAccepted = Cp6CapabilityState.Unknown)
{
    private readonly byte[] _consumerLock = consumerLock.ToArray();
    public byte[] ConsumerLock => _consumerLock.ToArray();
    public Cp6CapabilityState SourceFrozen { get; } = SourceFrozen;
    public Cp6CapabilityState ContractShapeValid { get; } = ContractShapeValid;
    public Cp6CapabilityState ArtifactBytesVerified { get; } = ArtifactBytesVerified;
    public Cp6CapabilityState TrustCurrentlyAccepted { get; } = TrustCurrentlyAccepted;
    public Cp6CompatibilityDecision Compatibility { get; } = Compatibility;
    public string PublicationStatus { get; } = PublicationStatus;
    public string TrustStatus { get; } = TrustStatus;
    public Cp6CapabilityState ConsumerQualified { get; } = ConsumerQualified;
    public Cp6CapabilityState HostRegistered { get; } = HostRegistered;
    public Cp6CapabilityState ConfigEnabled { get; } = ConfigEnabled;
    public Cp6CapabilityState RuntimeObserved { get; } = RuntimeObserved;
    public Cp6CapabilityState BusinessApplied { get; } = BusinessApplied;
    public Cp6CapabilityState IndependentlyAccepted { get; } = IndependentlyAccepted;
}
