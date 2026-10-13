using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;

namespace CP6.Platform.AspNetCore.HttpV2;

/// <summary>Host-owned immutable registration. References are evidence locators, not proof by themselves.</summary>
public sealed record Cp6HttpOperationBinding
{
    public Cp6HttpOperationBinding(string bindingId, string clientName, string owner, HttpMethod method,
        Uri destination, string credentialProfileRef, string configurationVersion, string routeEpoch,
        string policyVersion, string receiptContractRef, string queryContractRef, string retentionRef,
        string registrationEvidenceRef, string runtimeEvidenceRef, string hostId, string endpointContractRef,
        string tenantScopeRule, string inputDigestContractRef, string businessKeyDomain, string gatewayProfileVersion,
        bool oneTimeCredentialExchange = false)
    {
        foreach (var reference in new[] { bindingId, clientName, owner, credentialProfileRef, configurationVersion,
                     routeEpoch, policyVersion, receiptContractRef, queryContractRef, retentionRef,
                     registrationEvidenceRef, runtimeEvidenceRef, hostId, endpointContractRef, tenantScopeRule,
                     inputDigestContractRef, businessKeyDomain, gatewayProfileVersion }) Cp6HttpV2Profile.RequireReference(reference);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri || destination.Scheme != Uri.UriSchemeHttps ||
            destination.UserInfo.Length != 0 || destination.Fragment.Length != 0)
            throw new ArgumentException("Owner destinations must be an exact absolute HTTPS URI.", nameof(destination));
        if (!Cp6HttpV2Handler.IsWrite(method)) throw new ArgumentException("Binding requires an approved write method.");
        BindingId = bindingId; ClientName = clientName; Owner = owner; Method = method; Destination = destination;
        CredentialProfileRef = credentialProfileRef; ConfigurationVersion = configurationVersion;
        RouteEpoch = routeEpoch; PolicyVersion = policyVersion; ReceiptContractRef = receiptContractRef;
        QueryContractRef = queryContractRef; RetentionRef = retentionRef;
        RegistrationEvidenceRef = registrationEvidenceRef; RuntimeEvidenceRef = runtimeEvidenceRef;
        OneTimeCredentialExchange = oneTimeCredentialExchange;
        HostId = hostId; EndpointContractRef = endpointContractRef; TenantScopeRule = tenantScopeRule;
        InputDigestContractRef = inputDigestContractRef; BusinessKeyDomain = businessKeyDomain; GatewayProfileVersion = gatewayProfileVersion;
    }
    public string BindingId { get; }
    public string ClientName { get; }
    public string Owner { get; }
    public HttpMethod Method { get; }
    public Uri Destination { get; }
    public string CredentialProfileRef { get; }
    public string ConfigurationVersion { get; }
    public string RouteEpoch { get; }
    public string PolicyVersion { get; }
    public string ReceiptContractRef { get; }
    public string QueryContractRef { get; }
    public string RetentionRef { get; }
    public string RegistrationEvidenceRef { get; }
    public string RuntimeEvidenceRef { get; }
    public bool OneTimeCredentialExchange { get; }
    public string HostId { get; }
    public string EndpointContractRef { get; }
    public string TenantScopeRule { get; }
    public string InputDigestContractRef { get; }
    public string BusinessKeyDomain { get; }
    public string GatewayProfileVersion { get; }
}

/// <summary>Actual immutable transport inputs for the trusted verifier. Contains credentials: never log or return it.</summary>
public sealed class Cp6HttpRequestSnapshot
{
    private readonly byte[] body;
    internal Cp6HttpRequestSnapshot(HttpMethod method, Uri destination,
        IEnumerable<(string Key, string[] Values)> headers, IEnumerable<(string Key, string[] Values)> contentHeaders, byte[]? body)
    {
        Method = method; Destination = destination;
        this.body = body is null ? [] : (byte[])body.Clone();
        BodySha256 = Convert.ToHexString(SHA256.HashData(this.body));
        Headers = new ReadOnlyDictionary<string, IReadOnlyList<string>>(headers.ToDictionary(
            h => h.Key, h => (IReadOnlyList<string>)Array.AsReadOnly((string[])h.Values.Clone()), StringComparer.OrdinalIgnoreCase));
        ContentHeaders = new ReadOnlyDictionary<string, IReadOnlyList<string>>(contentHeaders
            .ToDictionary(h => h.Key, h => (IReadOnlyList<string>)Array.AsReadOnly((string[])h.Values.Clone()), StringComparer.OrdinalIgnoreCase));
    }
    public HttpMethod Method { get; }
    public Uri Destination { get; }
    public string BodySha256 { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Headers { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> ContentHeaders { get; }
    public byte[] CopyBody() => (byte[])body.Clone();
}

/// <summary>Immutable original identity. Bytes are copied on entry and exit; caller assertions remain untrusted.</summary>
public sealed class Cp6HttpOperationIdentity
{
    private readonly byte[] body;
    public Cp6HttpOperationIdentity(Cp6HttpOperationBinding binding, string tenant, string action,
        string key, string requestVersion, byte[] body)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(body);
        foreach (var value in new[] { tenant, action, requestVersion }) Cp6HttpV2Profile.RequireReference(value);
        if (!IsSafeKey(key)) throw new ArgumentException("A single bounded operation key is required.", nameof(key));
        Binding = binding; Tenant = tenant; Action = action; Key = key; RequestVersion = requestVersion;
        this.body = (byte[])body.Clone();
        BodySha256 = Convert.ToHexString(SHA256.HashData(this.body));
    }
    public Cp6HttpOperationBinding Binding { get; }
    public string Tenant { get; }
    public string Action { get; }
    public string Key { get; }
    public string RequestVersion { get; }
    public string BodySha256 { get; }
    public byte[] CopyBody() => (byte[])body.Clone();
    internal bool MatchesBody(byte[] candidate) => body.AsSpan().SequenceEqual(candidate);
    internal static bool IsSafeKey(string? value) => value is not null && Regex.IsMatch(value,
        "\\A[A-Za-z0-9][A-Za-z0-9._:-]{0,127}\\z", RegexOptions.CultureInvariant);
    public static readonly HttpRequestOptionsKey<Cp6HttpOperationIdentity> RequestOption = new("CP6.HttpV2.OriginalIdentity");
}

public enum Cp6OriginalResultState { Unknown, NotFound, NotAppliedProven, Applied, Rejected, Pending }
public sealed record Cp6OriginalResult(Cp6OriginalResultState State, HttpResponseMessage? Response = null);

/// <summary>
/// Trusted host adapter, resolved only from registration. Implementations must validate actual Owner evidence,
/// current historical read scope/field mask, and the original immutable identity. A replay precheck never
/// replaces the Owner's final authorization fence and atomic effect/receipt transaction.
/// </summary>
public interface ICp6HttpOperationAuthority
{
    ValueTask<bool> VerifyBindingAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request,
        Cp6HttpDeadline deadline, CancellationToken token);
    ValueTask<Cp6OriginalResult> QueryOriginalAsync(Cp6HttpOperationIdentity operation, Cp6HttpDeadline deadline, CancellationToken token);
    ValueTask<bool> AuthorizeReplayAsync(Cp6HttpOperationIdentity operation, Cp6HttpRequestSnapshot request,
        Cp6HttpDeadline deadline, CancellationToken token);
}
