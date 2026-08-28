using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CloudNative.CloudEvents;

namespace CP6.Platform.Messaging;

/// <summary>
/// Defines the required CP6 CloudEvents 1.0 extension attributes.
/// </summary>
public static partial class Cp6CloudEventAttributes
{
    public static readonly CloudEventAttribute TenantId = CloudEventAttribute.CreateExtension(
        "tenantid",
        CloudEventAttributeType.String,
        value =>
        {
            var tenantValue = (string)value;
            if (!Guid.TryParseExact(tenantValue, "D", out var tenantId) ||
                tenantId == Guid.Empty ||
                tenantValue != tenantId.ToString("D"))
            {
                throw new ArgumentException("tenantid must be a non-empty lowercase UUID.");
            }
        });

    public static readonly CloudEventAttribute CorrelationId = CreateProfiledString("correlationid", CorrelationPattern());

    public static readonly CloudEventAttribute CausationId = CreateProfiledString("causationid", IdentifierPattern());

    public static readonly CloudEventAttribute AggregateId = CreateProfiledString("aggregateid", IdentifierPattern());

    public static readonly CloudEventAttribute AggregateVersion = CloudEventAttribute.CreateExtension(
        "aggregateversion",
        CloudEventAttributeType.Integer,
        value =>
        {
            if ((int)value < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "aggregateversion must be positive.");
            }
        });

    public static readonly CloudEventAttribute SchemaVersion = CreateProfiledString("schemaversion", SemanticVersionPattern());

    public static readonly CloudEventAttribute Region = CreateProfiledString("region", RegionPattern());

    public static IReadOnlyList<CloudEventAttribute> All { get; } = new ReadOnlyCollection<CloudEventAttribute>(
        [TenantId, CorrelationId, CausationId, AggregateId, AggregateVersion, SchemaVersion, Region]);

    private static CloudEventAttribute CreateProfiledString(string name, Regex pattern) =>
        CloudEventAttribute.CreateExtension(
            name,
            CloudEventAttributeType.String,
            value =>
            {
                if (!pattern.IsMatch((string)value))
                {
                    throw new ArgumentException($"{name} does not match the CP6 event profile.");
                }
            });

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex CorrelationPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._:-]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)\\.(?:0|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticVersionPattern();

    [GeneratedRegex("^[a-z][a-z0-9-]{1,31}$", RegexOptions.CultureInvariant)]
    private static partial Regex RegionPattern();
}
