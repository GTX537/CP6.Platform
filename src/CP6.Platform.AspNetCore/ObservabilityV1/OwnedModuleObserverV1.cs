using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using CP6.Platform.Contracts.ObservabilityV1;

namespace CP6.Platform.AspNetCore.ObservabilityV1;

/// <summary>Owns the exact byte copies passed to LoadFromStream. Scope excludes dependencies, native code, host image and packages.</summary>
public sealed class OwnedModuleObserverV1 : IDisposable
{
    private readonly AssemblyLoadContext context = new("cp6-owned-observation", isCollectible: true);
    private readonly List<(byte[] Bytes, Assembly Assembly)> modules = [];
    private int disposed;
    public OwnedModuleObserverV1(RuntimeIdentityBindingV1 binding, string source, IEnumerable<byte[]> images, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(binding); ArgumentNullException.ThrowIfNull(images);
        Binding = binding; Source = ObservabilityValueV1.Identifier(source);
        try
        {
            ObservedUtc = ObservabilityValueV1.Utc((timeProvider ?? TimeProvider.System).GetUtcNow());
            foreach (var image in images.Take(33))
            {
                if (modules.Count >= 32 || image is null || image.Length is < 1 or > 16_777_216 || modules.Sum(m => (long)m.Bytes.Length) + image.Length > 33_554_432)
                    throw new ArgumentException("Owned module inventory exceeds its bound.");
                var owned = image.ToArray();
                using var stream = new MemoryStream(owned, writable: false);
                var assembly = context.LoadFromStream(stream);
                if (modules.Any(m => m.Assembly.GetName().Name == assembly.GetName().Name)) throw new ArgumentException("Duplicate module.");
                modules.Add((owned, assembly));
            }
            if (modules.Count == 0) throw new ArgumentException("No owned modules.");
        }
        catch
        {
            Dispose();
            // Loader errors can contain paths or assembly identity. This public boundary emits neither.
            throw new ArgumentException("Owned module loading failed.");
        }
    }
    internal RuntimeIdentityBindingV1 Binding { get; }
    internal string Source { get; }
    internal DateTimeOffset ObservedUtc { get; }
    internal IReadOnlyList<ModulePinV1>? Snapshot()
    {
        if (Volatile.Read(ref disposed) != 0) return null;
        return new ReadOnlyCollection<ModulePinV1>(modules.Select(m => new ModulePinV1(m.Assembly.GetName().Name!,
            m.Assembly.GetName().Version!.ToString(), "sha256:" + Convert.ToHexString(SHA256.HashData(m.Bytes)).ToLowerInvariant(), SourceRevision(m.Assembly))).ToArray());
    }
    private static string? SourceRevision(Assembly assembly)
    {
        // Read metadata without invoking an attribute constructor or echoing arbitrary informational text.
        var information = assembly.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == typeof(AssemblyInformationalVersionAttribute).FullName);
        var revision = (information?.ConstructorArguments.FirstOrDefault().Value as string)?.Split('+').LastOrDefault();
        return revision is { Length: 40 } && revision.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f') ? revision : null;
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        context.Unload();
    }
}

public sealed class RuntimeIdentityObservationV1
{
    internal RuntimeIdentityObservationV1(IdentityVerdictV1 verdict, DateTimeOffset at, IReadOnlyList<ModulePinV1>? modules = null,
        IReadOnlyDictionary<string, IdentityVerdictV1>? moduleVerdicts = null)
    {
        Verdict = verdict; ObservedUtc = at; Modules = modules ?? Array.Empty<ModulePinV1>();
        ModuleVerdicts = new ReadOnlyDictionary<string, IdentityVerdictV1>(new Dictionary<string, IdentityVerdictV1>(moduleVerdicts ?? new Dictionary<string, IdentityVerdictV1>()));
    }
    public string ContractVersion => "1.0.0";
    public string Scope => "owned-managed-modules-only";
    public string Verifier => "owned-byte-loader-v1";
    public string EvidenceRef => "local-owned-modules";
    public IdentityVerdictV1 Verdict { get; }
    public DateTimeOffset ObservedUtc { get; }
    public IReadOnlyList<ModulePinV1> Modules { get; }
    public IReadOnlyDictionary<string, IdentityVerdictV1> ModuleVerdicts { get; }
    public EvidenceStatusV1 CurrentPackageTrust => EvidenceStatusV1.Unproven;
    public EvidenceStatusV1 SourceProvenance => EvidenceStatusV1.Unproven;
    public EvidenceStatusV1 DeploymentAttestation => EvidenceStatusV1.Unproven;
}

/// <summary>Only the sealed byte loader can supply positive scoped evidence; arbitrary caller reports cannot.</summary>
public sealed class RuntimeIdentityEvaluatorV1(RuntimeManifestV1? expected, OwnedModuleObserverV1? observer, TimeProvider? timeProvider = null)
{
    public RuntimeIdentityObservationV1 Observe(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            now = ObservabilityValueV1.Utc((timeProvider ?? TimeProvider.System).GetUtcNow());
            if (cancellationToken.IsCancellationRequested || expected is null || observer is null || expected.Kind != ObservedArtifactKindV1.OwnedManagedModule)
                return new(IdentityVerdictV1.Unproven, now);
            var actual = observer.Snapshot();
            if (actual is null || observer.ObservedUtc.Offset != TimeSpan.Zero || observer.ObservedUtc <= DateTimeOffset.UnixEpoch || observer.ObservedUtc > now || now - observer.ObservedUtc > expected.Binding.Freshness ||
                observer.Binding.StartedUtc > observer.ObservedUtc || observer.Binding.StartedUtc > now)
                return new(IdentityVerdictV1.Unproven, now);
            var commonBindingMatches = observer.Source == expected.Source && SameBinding(expected.Binding, observer.Binding);
            var moduleVerdicts = expected.Modules.ToDictionary(pin => pin.AssemblyName, pin =>
                commonBindingMatches && actual.Any(module => module.AssemblyName == pin.AssemblyName && module.AssemblyVersion == pin.AssemblyVersion && module.Digest == pin.Digest &&
                    (pin.SourceRevision is null || pin.SourceRevision == module.SourceRevision)) ? IdentityVerdictV1.Match : IdentityVerdictV1.Mismatch, StringComparer.Ordinal);
            var matches = commonBindingMatches && actual.Count == expected.Modules.Count && moduleVerdicts.Values.All(verdict => verdict == IdentityVerdictV1.Match);
            return new(matches ? IdentityVerdictV1.Match : IdentityVerdictV1.Mismatch, observer.ObservedUtc, actual, moduleVerdicts);
        }
        catch { return new(IdentityVerdictV1.Unproven, now); }
    }
    private static bool SameBinding(RuntimeIdentityBindingV1 expectedBinding, RuntimeIdentityBindingV1 actual) =>
        expectedBinding.ContractVersion == actual.ContractVersion && expectedBinding.Declared == actual.Declared &&
        expectedBinding.Environment == actual.Environment && expectedBinding.Region == actual.Region && expectedBinding.PackageSetRef == actual.PackageSetRef &&
        expectedBinding.ConfigurationDigest == actual.ConfigurationDigest && expectedBinding.SchemaSetRef == actual.SchemaSetRef && expectedBinding.DeploymentRef == actual.DeploymentRef &&
        expectedBinding.ProcessInstance == actual.ProcessInstance && expectedBinding.Epoch == actual.Epoch && expectedBinding.StartedUtc == actual.StartedUtc;
}
