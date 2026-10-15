namespace CP6.Platform.Messaging.TransportV1;

public sealed class TransportRouteV1
{
    public const string GenericProfile = "cp6-generic-p05-v1";
    public const int GenericMaxBytes = 1_048_576;
    public TransportRouteV1(string profile, string route, string eventType, string schemaId,
        string schemaVersion, string schemaSha256, Cp6ContractBundle bundle,
        string producer, string source, string topic, int maxBytes = GenericMaxBytes)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (profile != GenericProfile || !bundle.IsTransportIsolated)
            throw new ArgumentException("Only explicit isolated generic transport bundles are enabled.");
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(producer);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (route.Length > 128 || route.Any(char.IsControl) || maxBytes is < 1 or > GenericMaxBytes)
            throw new ArgumentException("Invalid route bounds.");
        if (!bundle.TryResolve(eventType, schemaId, schemaVersion, out var entry, out _) ||
            entry.SchemaSha256 != schemaSha256)
            throw new ArgumentException("The exact contract tuple and hash must be present in the fixed bundle.");
        var identity = Cp6EventContractIdentity.Parse(eventType);
        if (identity.Producer != producer || Cp6DaprKafkaConventions.GetTopic(identity) != topic ||
            !Uri.TryCreate(source, UriKind.Absolute, out _))
            throw new ArgumentException("The configured producer or topic is not canonical for this contract.");
        Profile = profile; Route = route; EventType = eventType; SchemaId = schemaId;
        SchemaVersion = schemaVersion; SchemaSha256 = schemaSha256; Producer = producer;
        Source = source; Topic = topic; MaxBytes = maxBytes;
        Validator = new Cp6CloudEventValidator(bundle);
    }

    internal Cp6CloudEventValidator Validator { get; }

    public string Profile { get; } = "";
    public string Route { get; } = "";
    public string EventType { get; } = "";
    public string SchemaId { get; } = "";
    public string SchemaVersion { get; } = "";
    public string SchemaSha256 { get; } = "";
    public string Producer { get; } = "";
    public string Source { get; } = "";
    public string Topic { get; } = "";
    public int MaxBytes { get; }
}
