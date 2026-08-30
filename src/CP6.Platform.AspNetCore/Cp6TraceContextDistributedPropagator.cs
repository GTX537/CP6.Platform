using System.Diagnostics;

namespace CP6.Platform.AspNetCore;

internal sealed class Cp6TraceContextDistributedPropagator : DistributedContextPropagator
{
    private static readonly IReadOnlyCollection<string> TraceFields =
        Array.AsReadOnly(["traceparent", "tracestate"]);
    private readonly DistributedContextPropagator inner;

    internal static Cp6TraceContextDistributedPropagator Instance { get; } =
        new(CreateDefaultPropagator());

    internal Cp6TraceContextDistributedPropagator(DistributedContextPropagator inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        this.inner = inner;
    }

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

        inner.Inject(activity, carrier, (target, fieldName, fieldValue) =>
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
        out string? traceState)
    {
        if (getter is null)
        {
            traceParent = null;
            traceState = null;
            return;
        }

        inner.ExtractTraceIdAndState(
            carrier,
            (object? target,
                string fieldName,
                out string? fieldValue,
                out IEnumerable<string>? fieldValues) =>
            {
                if (!TraceFields.Contains(fieldName, StringComparer.OrdinalIgnoreCase))
                {
                    fieldValue = null;
                    fieldValues = null;
                    return;
                }

                getter(target, fieldName, out fieldValue, out fieldValues);
            },
            out traceParent,
            out traceState);
    }

    public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
        object? carrier,
        PropagatorGetterCallback? getter) => null;
}
