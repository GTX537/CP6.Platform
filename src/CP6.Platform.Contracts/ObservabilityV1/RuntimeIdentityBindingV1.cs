using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace CP6.Platform.Contracts.ObservabilityV1;

public enum IdentityVerdictV1 { Unproven, Match, Mismatch }
public enum ObservedArtifactKindV1 { OwnedManagedModule, ProcessImage, ContainerImage, PackageSet }
public enum EvidenceStatusV1 { Unproven, Observed, NotEvaluated }
public enum TelemetryCompletenessV1 { Incomplete, LocallyObserved }

/// <summary>Shared bounded wire validation. Rejected values are never included in errors.</summary>
public static class ObservabilityValueV1
{
    public static string Identifier(string value)
    {
        if (value is null || value.Length is < 1 or > 64 || !Regex.IsMatch(value, @"\A[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("A canonical bounded identifier is required.");
        return value;
    }

    public static string Digest(string value)
    {
        if (value is null || !Regex.IsMatch(value, @"\Asha256:[a-f0-9]{64}\z", RegexOptions.CultureInvariant))
            throw new ArgumentException("A canonical SHA-256 digest is required.");
        return value;
    }

    public static DateTimeOffset Utc(DateTimeOffset value)
    {
        if (value.Offset != TimeSpan.Zero || value <= DateTimeOffset.UnixEpoch) throw new ArgumentException("A UTC timestamp is required.");
        return value;
    }

    public static TimeSpan Window(TimeSpan value)
    {
        if (value <= TimeSpan.Zero || value > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(value));
        return value;
    }
}

public sealed class RuntimeIdentityBindingV1
{
    public RuntimeIdentityBindingV1(Cp6ReleaseIdentity declared, string environment, string region, string packageSetRef,
        string configurationDigest, string schemaSetRef, string deploymentRef, string processInstance, long epoch,
        DateTimeOffset startedUtc, TimeSpan freshness)
    {
        ArgumentNullException.ThrowIfNull(declared);
        declared.Validate();
        Declared = declared with { };
        Environment = ObservabilityValueV1.Identifier(environment);
        Region = ObservabilityValueV1.Identifier(region);
        PackageSetRef = ObservabilityValueV1.Identifier(packageSetRef);
        ConfigurationDigest = ObservabilityValueV1.Digest(configurationDigest);
        SchemaSetRef = ObservabilityValueV1.Identifier(schemaSetRef);
        DeploymentRef = ObservabilityValueV1.Identifier(deploymentRef);
        ProcessInstance = ObservabilityValueV1.Identifier(processInstance);
        if (epoch <= 0) throw new ArgumentOutOfRangeException(nameof(epoch));
        Epoch = epoch;
        StartedUtc = ObservabilityValueV1.Utc(startedUtc);
        Freshness = ObservabilityValueV1.Window(freshness);
    }
    public string ContractVersion => "1.0.0";
    public Cp6ReleaseIdentity Declared { get; }
    public string Environment { get; }
    public string Region { get; }
    public string PackageSetRef { get; }
    public string ConfigurationDigest { get; }
    public string SchemaSetRef { get; }
    public string DeploymentRef { get; }
    public string ProcessInstance { get; }
    public long Epoch { get; }
    public DateTimeOffset StartedUtc { get; }
    public TimeSpan Freshness { get; }
}

public sealed class ModulePinV1
{
    public ModulePinV1(string assemblyName, string assemblyVersion, string digest, string? sourceRevision = null)
    {
        if (assemblyName is null || assemblyName.Length is < 1 or > 128 || !Regex.IsMatch(assemblyName, @"\A[A-Za-z][A-Za-z0-9.]*\z")) throw new ArgumentException("Invalid module name.");
        if (!Version.TryParse(assemblyVersion, out var version) || version.ToString() != assemblyVersion) throw new ArgumentException("Invalid module version.");
        if (sourceRevision is not null && !Regex.IsMatch(sourceRevision, @"\A[a-f0-9]{40}\z", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid source revision.");
        AssemblyName = assemblyName; AssemblyVersion = assemblyVersion; Digest = ObservabilityValueV1.Digest(digest);
        SourceRevision = sourceRevision;
    }
    public string AssemblyName { get; }
    public string AssemblyVersion { get; }
    public string Digest { get; }
    public string? SourceRevision { get; }
}

/// <summary>Pins supplied through the host's trusted configuration channel, never obtained from an observer.</summary>
public sealed class RuntimeManifestV1
{
    public RuntimeManifestV1(RuntimeIdentityBindingV1 binding, ObservedArtifactKindV1 kind, string source, IEnumerable<ModulePinV1> modules)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(modules);
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        Binding = binding; Kind = kind; Source = ObservabilityValueV1.Identifier(source);
        var copy = modules.Take(33).ToArray();
        if (copy.Length is < 1 or > 32 || copy.Any(x => x is null) || copy.Select(x => x.AssemblyName).Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("A complete unique bounded module inventory is required.");
        Modules = new ReadOnlyCollection<ModulePinV1>(copy);
    }
    public RuntimeIdentityBindingV1 Binding { get; }
    public ObservedArtifactKindV1 Kind { get; }
    public string Source { get; }
    public IReadOnlyList<ModulePinV1> Modules { get; }
}

/// <summary>Local observation is never a release, package provenance or SLO acceptance decision.</summary>
public sealed class ObservationEnvelopeV1
{
    public ObservationEnvelopeV1(IdentityVerdictV1 runtimeObservation, TelemetryCompletenessV1 telemetry)
    {
        if (!Enum.IsDefined(runtimeObservation)) throw new ArgumentOutOfRangeException(nameof(runtimeObservation));
        if (!Enum.IsDefined(telemetry)) throw new ArgumentOutOfRangeException(nameof(telemetry));
        RuntimeObservation = runtimeObservation; Telemetry = telemetry;
    }
    public IdentityVerdictV1 RuntimeObservation { get; }
    public TelemetryCompletenessV1 Telemetry { get; }
    public string ContractVersion => "1.0.0";
    public string Scope => "local-nonproduction";
    public EvidenceStatusV1 SourceProvenance => EvidenceStatusV1.Unproven;
    public EvidenceStatusV1 DeclaredIdentity => EvidenceStatusV1.Observed;
    public EvidenceStatusV1 CurrentPackageTrust => EvidenceStatusV1.Unproven;
    public EvidenceStatusV1 HostRegistration => EvidenceStatusV1.NotEvaluated;
    public EvidenceStatusV1 BusinessAcceptance => EvidenceStatusV1.NotEvaluated;
    public EvidenceStatusV1 IndependentQualification => EvidenceStatusV1.NotEvaluated;
    public bool ProductionAccepted => false;
}
