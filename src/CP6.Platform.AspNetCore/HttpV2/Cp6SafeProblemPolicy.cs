using CP6.Platform.Contracts;

namespace CP6.Platform.AspNetCore.HttpV2;

/// <summary>Host registered definition, exact endpoint-visible fields, and safe message-code to text mappings.</summary>
public sealed class Cp6SafeProblemPolicy
{
    private readonly Dictionary<string, Dictionary<string, string>> fields;
    public Cp6SafeProblemPolicy(string policyId, Cp6ProblemDefinition definition,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> fields)
    {
        Cp6HttpV2Profile.RequireReference(policyId); ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(fields);
        PolicyId = policyId; Definition = definition;
        this.fields = new(StringComparer.Ordinal);
        foreach (var field in fields)
        {
            Cp6HttpV2Profile.RequireReference(field.Key);
            var messages = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var message in field.Value)
            {
                Cp6HttpV2Profile.RequireReference(message.Key);
                if (string.IsNullOrEmpty(message.Value) || message.Value.Any(char.IsControl))
                    throw new ArgumentException("Safe messages must be registered nonempty text without controls.");
                messages.Add(message.Key, message.Value);
            }
            this.fields.Add(field.Key, messages);
        }
    }
    public string PolicyId { get; }
    public Cp6ProblemDefinition Definition { get; }
    internal bool ContainsField(string field) => fields.ContainsKey(field);
    internal bool TryResolve(string field, string code, out string text)
    {
        text = string.Empty;
        return fields.TryGetValue(field, out var messages) && messages.TryGetValue(code, out text!);
    }
}
