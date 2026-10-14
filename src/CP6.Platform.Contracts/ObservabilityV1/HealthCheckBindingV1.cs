using System.Collections.ObjectModel;

namespace CP6.Platform.Contracts.ObservabilityV1;

public enum HealthPurposeV1 { Startup, Ready }
public enum DependencyKindV1 { Local, Database, RemoteService }
public enum ProbePolicyV1 { ReadOnly }
public enum CapabilityV1 { Read, NewEffect }
public enum ProbeFailureV1 { None, Failed, TimedOut, Cancelled, Stale, Busy, Missing }

public sealed class HealthCheckBindingV1
{
    public HealthCheckBindingV1(string registrationName, string safeName, HealthPurposeV1 purpose, string requiredTag,
        DependencyKindV1 dependency, TimeSpan timeout, TimeSpan freshness, ProbePolicyV1 probePolicy, bool publish)
    {
        RegistrationName = ObservabilityValueV1.Identifier(registrationName); SafeName = ObservabilityValueV1.Identifier(safeName);
        RequiredTag = ObservabilityValueV1.Identifier(requiredTag);
        if (!Enum.IsDefined(purpose) || !Enum.IsDefined(dependency) || probePolicy != ProbePolicyV1.ReadOnly) throw new ArgumentException("Invalid probe binding.");
        Purpose = purpose; Dependency = dependency; Timeout = ObservabilityValueV1.Window(timeout); Freshness = ObservabilityValueV1.Window(freshness);
        if (timeout > TimeSpan.FromSeconds(10)) throw new ArgumentException("Probe timeout exceeds bound.");
        ProbePolicy = probePolicy; Publish = publish;
    }
    public string ContractVersion => "1.0.0";
    public string RegistrationName { get; }
    public string SafeName { get; }
    public HealthPurposeV1 Purpose { get; }
    public string RequiredTag { get; }
    public DependencyKindV1 Dependency { get; }
    public TimeSpan Timeout { get; }
    public TimeSpan Freshness { get; }
    public ProbePolicyV1 ProbePolicy { get; }
    public bool Publish { get; }
}

public sealed class CapabilityRouteV1
{
    public CapabilityRouteV1(string template, CapabilityV1 capability, string? requiredModule = null, IEnumerable<string>? requiredChecks = null)
    {
        if (template is null || template.Length is < 1 or > 128 || !template.StartsWith('/') || template.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '{' or '}' or '.')) || !Enum.IsDefined(capability))
            throw new ArgumentException("Invalid capability route.");
        Template = template; Capability = capability;
        if (capability == CapabilityV1.NewEffect && (requiredModule is null || requiredModule.Length is < 1 or > 128 || !System.Text.RegularExpressions.Regex.IsMatch(requiredModule, @"\A[A-Za-z][A-Za-z0-9.]*\z")))
            throw new ArgumentException("New-effect capability requires its owned module identity binding.");
        if (capability == CapabilityV1.Read && requiredModule is not null) throw new ArgumentException("Read capability does not grant an identity-based write capability.");
        RequiredModule = requiredModule;
        var checks = requiredChecks?.Take(33).Select(ObservabilityValueV1.Identifier).ToArray();
        if (checks is null || checks.Length is < 1 or > 32 || checks.Distinct(StringComparer.Ordinal).Count() != checks.Length)
            throw new ArgumentException("Each capability route requires a unique nonempty check inventory.");
        RequiredChecks = Array.AsReadOnly(checks);
    }
    public string Template { get; }
    public CapabilityV1 Capability { get; }
    public string? RequiredModule { get; }
    public IReadOnlyList<string> RequiredChecks { get; }
}

public sealed class HealthProfileV1
{
    public HealthProfileV1(string permissionPolicy, IEnumerable<HealthCheckBindingV1> checks, IEnumerable<CapabilityRouteV1> routes)
    {
        PermissionPolicy = ObservabilityValueV1.Identifier(permissionPolicy);
        ArgumentNullException.ThrowIfNull(checks); ArgumentNullException.ThrowIfNull(routes);
        var checkCopy = checks.Take(33).ToArray(); var routeCopy = routes.Take(65).ToArray();
        if (checkCopy.Length is < 1 or > 32 || checkCopy.Any(x => x is null) || checkCopy.Select(x => x.RegistrationName).Distinct().Count() != checkCopy.Length || checkCopy.Select(x => x.SafeName).Distinct().Count() != checkCopy.Length ||
            routeCopy.Length is < 1 or > 64 || routeCopy.Any(x => x is null) || routeCopy.Select(x => x.Template).Distinct(StringComparer.OrdinalIgnoreCase).Count() != routeCopy.Length)
            throw new ArgumentException("A nonempty unique bounded health and route inventory is required.");
        if (routeCopy.Any(route => route.RequiredChecks.Any(name => !checkCopy.Any(check => check.RegistrationName == name && check.Purpose == HealthPurposeV1.Ready))))
            throw new ArgumentException("Route check inventory must reference registered readiness bindings.");
        Checks = new ReadOnlyCollection<HealthCheckBindingV1>(checkCopy); Routes = new ReadOnlyCollection<CapabilityRouteV1>(routeCopy);
    }
    public string PermissionPolicy { get; }
    public IReadOnlyList<HealthCheckBindingV1> Checks { get; }
    public IReadOnlyList<CapabilityRouteV1> Routes { get; }
}
