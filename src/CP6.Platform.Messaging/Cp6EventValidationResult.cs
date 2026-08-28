using CloudNative.CloudEvents;

namespace CP6.Platform.Messaging;

public enum Cp6EventValidationFailure
{
    None,
    MalformedJson,
    UnknownContract,
    SchemaMismatch,
    InvalidCloudEvent
}

/// <summary>
/// Returns a fail-closed result without copying event values into errors.
/// </summary>
public sealed record Cp6EventValidationResult(
    bool IsValid,
    Cp6EventValidationFailure Failure,
    IReadOnlyList<string> InstanceLocations,
    CloudEvent? CloudEvent)
{
    public const string ErrorCode = "CP6_EVENT_SCHEMA_INVALID";

    public static Cp6EventValidationResult Success(CloudEvent cloudEvent) =>
        new(true, Cp6EventValidationFailure.None, [], cloudEvent);

    public static Cp6EventValidationResult Invalid(Cp6EventValidationFailure failure, params string[] locations) =>
        new(false, failure, locations.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), null);
}
