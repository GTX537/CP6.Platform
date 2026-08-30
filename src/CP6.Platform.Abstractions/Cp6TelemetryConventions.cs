using System.Collections.Frozen;
using System.Collections.ObjectModel;

namespace CP6.Platform.Abstractions;

/// <summary>
/// Stable CP6 ActivitySource names consumed by OpenTelemetry registration.
/// </summary>
public static class Cp6TelemetrySources
{
    public const string AspNetCore = "CP6.Platform.AspNetCore";
    public const string Messaging = "CP6.Platform.Messaging";
    public const string EntityFramework = "CP6.Platform.EntityFramework";

    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>([AspNetCore, Messaging, EntityFramework]);
}

/// <summary>
/// Stable CP6 Meter names consumed by OpenTelemetry registration.
/// </summary>
public static class Cp6TelemetryMeters
{
    public const string AspNetCore = "CP6.Platform.AspNetCore";
    public const string Messaging = "CP6.Platform.Messaging";
    public const string EntityFramework = "CP6.Platform.EntityFramework";

    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>([AspNetCore, Messaging, EntityFramework]);
}

/// <summary>
/// Low-cardinality names and values that form the CP6 telemetry contract.
/// </summary>
public static class Cp6TelemetryConventions
{
    public const string HttpOutboundOperation = "cp6.http.outbound";
    public const string DaprInvokeOperation = "cp6.messaging.dapr.invoke";
    public const string MessagingPublishOperation = "cp6.messaging.publish";
    public const string MessagingConsumeOperation = "cp6.messaging.consume";
    public const string OutboxDispatchOperation = "cp6.outbox.dispatch";
    public const string InboxProcessOperation = "cp6.inbox.process";

    public const string RegionTag = "cp6.region";
    public const string OperationTag = "cp6.operation";
    public const string OutcomeTag = "cp6.outcome";
    public const string ErrorCodeTag = "cp6.error.code";
    public const string MessagingTransportTag = "cp6.messaging.transport";
    public const string MessagingDispositionTag = "cp6.messaging.disposition";
    public const string HttpOperationKindTag = "cp6.http.operation_kind";

    public static IReadOnlyList<string> AllOperations { get; } =
        new ReadOnlyCollection<string>(
            [
                HttpOutboundOperation,
                DaprInvokeOperation,
                MessagingPublishOperation,
                MessagingConsumeOperation,
                OutboxDispatchOperation,
                InboxProcessOperation
            ]);

    public static IReadOnlySet<string> AllowedMetricTags { get; } =
        new[]
        {
            RegionTag,
            OperationTag,
            OutcomeTag,
            ErrorCodeTag,
            MessagingTransportTag,
            MessagingDispositionTag,
            HttpOperationKindTag
        }.ToFrozenSet(StringComparer.Ordinal);

    public static void EnsureAllowedMetricTag(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (!AllowedMetricTags.Contains(name))
        {
            throw new ArgumentException("Metric tag is not in the CP6 low-cardinality allowlist.", nameof(name));
        }
    }

}

/// <summary>
/// Stable health-check tags used to separate liveness, startup, and readiness.
/// </summary>
public static class Cp6HealthTags
{
    public const string Live = "live";
    public const string Startup = "startup";
    public const string Ready = "ready";

    public static IReadOnlyList<string> All { get; } =
        new ReadOnlyCollection<string>([Live, Startup, Ready]);
}
