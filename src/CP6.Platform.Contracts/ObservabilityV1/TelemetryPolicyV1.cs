using System.Collections.Frozen;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace CP6.Platform.Contracts.ObservabilityV1;

/// <summary>Closed attribute vocabulary and finite deployment-approved values. Case sensitive by design.</summary>
public sealed class TelemetryPolicyV1
{
    private readonly FrozenDictionary<string, FrozenSet<string>> values;
    public TelemetryPolicyV1(string service, string environment, string region, IEnumerable<string> routeTemplates)
    {
        ArgumentNullException.ThrowIfNull(routeTemplates);
        var routes = routeTemplates.Take(65).ToArray();
        if (routes.Length > 64 || routes.Any(x => x is null || x.Length > 128 || !Regex.IsMatch(x, @"\A/[a-zA-Z0-9{}/._-]*\z")) || routes.Distinct(StringComparer.Ordinal).Count() != routes.Length)
            throw new ArgumentException("A bounded unique route-template inventory is required.");
        Resources = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>
        {
            ["service.name"] = ObservabilityValueV1.Identifier(service),
            ["deployment.environment.name"] = ObservabilityValueV1.Identifier(environment),
            ["cp6.region"] = ObservabilityValueV1.Identifier(region)
        });
        values = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["http.request.method"] = ["GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS"],
            ["http.route"] = routes,
            ["http.response.status_class"] = ["1xx", "2xx", "3xx", "4xx", "5xx"],
            ["cp6.outcome"] = ["success", "failure", "cancelled", "unknown", "rejected"],
            ["cp6.operation"] = ["cp6.http.outbound", "cp6.messaging.dapr.invoke", "cp6.messaging.publish", "cp6.messaging.consume", "cp6.outbox.dispatch", "cp6.inbox.process"],
            ["cp6.messaging.disposition"] = ["published", "invoked", "consumed", "drop", "failed", "cancelled", "enqueue", "claim", "applied", "invalid", "duplicate", "payload_conflict", "ignored_out_of_order", "retry_scheduled", "dead_lettered", "replayed", "retained"],
            ["cp6.messaging.transport"] = ["dapr", "kafka", "http"],
            ["cp6.http.operation_kind"] = ["read", "write"],
            ["cp6.error.code"] = ["unknown", "timeout", "cancelled", "unavailable", "event_contract_invalid", "topic_mismatch", "partition_key_mismatch", "transport_failure", "invocation_failure", "operation_incomplete", "validation_failed", "publish_failure", "processing_failure", "payload_conflict", "out_of_order", "invalid_trace_context"],
            ["cp6.region"] = [region],
            ["log.severity"] = ["Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"]
        }.ToFrozenDictionary(x => x.Key, x => x.Value.ToFrozenSet(StringComparer.Ordinal), StringComparer.Ordinal);
    }
    public string ContractVersion => "1.0.0";
    public IReadOnlyDictionary<string, string> Resources { get; }
    public bool ExternalExportEnabled => false;
    public bool Allows(string? key, string? value) => key is { Length: >= 1 and <= 64 } && value is { Length: <= 128 } &&
        values.TryGetValue(key, out var registered) && registered.Contains(value);
    public IReadOnlyDictionary<string, string> Sanitize(IEnumerable<KeyValuePair<string, object?>> attributes)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in attributes.Take(32))
        {
            if (result.Count >= 12) break;
            if (pair.Value is string value && value.Length <= 128 && Allows(pair.Key, value)) result.TryAdd(pair.Key, value);
        }
        return new ReadOnlyDictionary<string, string>(result);
    }
}
