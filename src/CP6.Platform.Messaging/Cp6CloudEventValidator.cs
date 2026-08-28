using System.Text.Json;
using Json.Schema;

namespace CP6.Platform.Messaging;

/// <summary>
/// Validates the complete structured CloudEvent against the exact contract bundle entry.
/// </summary>
public sealed class Cp6CloudEventValidator
{
    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true
    };

    private readonly Cp6ContractBundle bundle;

    public Cp6CloudEventValidator(Cp6ContractBundle bundle)
    {
        this.bundle = bundle ?? throw new ArgumentNullException(nameof(bundle));
    }

    public Cp6EventValidationResult Validate(ReadOnlyMemory<byte> structuredEvent)
    {
        if (structuredEvent.IsEmpty)
        {
            return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.MalformedJson, "");
        }

        try
        {
            using var document = JsonDocument.Parse(structuredEvent, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetRequiredString(root, "type", out var eventType) ||
                !TryGetRequiredString(root, "dataschema", out var schemaId) ||
                !TryGetRequiredString(root, "schemaversion", out var schemaVersion))
            {
                return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.UnknownContract, "/type", "/dataschema", "/schemaversion");
            }

            if (!bundle.TryResolve(eventType, schemaId, schemaVersion, out _, out var schema))
            {
                return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.UnknownContract, "/type", "/dataschema", "/schemaversion");
            }

            var schemaResult = schema.Evaluate(root, EvaluationOptions);
            if (!schemaResult.IsValid)
            {
                return Cp6EventValidationResult.Invalid(
                    Cp6EventValidationFailure.SchemaMismatch,
                    CollectInvalidLocations(schemaResult).ToArray());
            }

            var cloudEvent = Cp6CloudEventCodec.DecodeStructured(structuredEvent);
            return Cp6EventValidationResult.Success(cloudEvent);
        }
        catch (JsonException)
        {
            return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.MalformedJson, "");
        }
        catch (ArgumentException)
        {
            return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.InvalidCloudEvent, "");
        }
        catch (InvalidOperationException)
        {
            return Cp6EventValidationResult.Invalid(Cp6EventValidationFailure.InvalidCloudEvent, "");
        }
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString()!);
    }

    private static IEnumerable<string> CollectInvalidLocations(EvaluationResults result)
    {
        var details = result.Details;
        if (!result.IsValid && (details is null || details.Count == 0))
        {
            yield return result.InstanceLocation.ToString();
        }

        if (details is null)
        {
            yield break;
        }

        foreach (var detail in details)
        {
            foreach (var location in CollectInvalidLocations(detail))
            {
                yield return location;
            }
        }
    }
}
