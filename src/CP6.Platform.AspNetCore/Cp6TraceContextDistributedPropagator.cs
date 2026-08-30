using System.Diagnostics;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6TraceContextDistributedPropagator : DistributedContextPropagator
{
    private static readonly DistributedContextPropagator Inner = CreateDefaultPropagator();
    private static readonly string[] TraceFields = ["traceparent", "tracestate"];

    internal static Cp6TraceContextDistributedPropagator Instance { get; } = new();

    public override IReadOnlyCollection<string> Fields => TraceFields;

    public override void Inject(
        Activity? activity,
        object? carrier,
        PropagatorSetterCallback? setter)
    {
        if (setter is null)
        {
            return;
        }

        Inner.Inject(activity, carrier, (target, fieldName, fieldValue) =>
        {
            if (TraceFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
            {
                setter(target, fieldName, fieldValue);
            }
        });
    }

    public override void ExtractTraceIdAndState(
        object? carrier,
        PropagatorGetterCallback? getter,
        out string? traceParent,
        out string? traceState) =>
        Inner.ExtractTraceIdAndState(carrier, getter, out traceParent, out traceState);

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
        object? carrier,
        PropagatorGetterCallback? getter) => null;
}
