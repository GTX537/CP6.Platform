using System.Diagnostics;
using CP6.Platform.AspNetCore;

namespace CP6.Platform.AspNetCoreTests;

public sealed class TraceContextDistributedPropagatorTests
{
    [Fact]
    public void Fields_AreReadOnlyAndContainOnlyW3cTraceFields()
    {
        var propagator = new Cp6TraceContextDistributedPropagator(new LegacyCompatibleTestPropagator());
        var fields = Assert.IsAssignableFrom<ICollection<string>>(propagator.Fields);

        Assert.True(fields.IsReadOnly);
        Assert.Equal(new[] { "traceparent", "tracestate" }, fields);
        Assert.Throws<NotSupportedException>(() => fields.Add("Request-Id"));
    }

    [Fact]
    public void ExtractTraceIdAndState_DoesNotExposeLegacyRequestIdToInnerPropagator()
    {
        var propagator = new Cp6TraceContextDistributedPropagator(new LegacyCompatibleTestPropagator());
        var carrier = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Request-Id"] = ["|legacy-root.1."]
        };

        propagator.ExtractTraceIdAndState(carrier, GetHeaderValues, out var traceParent, out var traceState);

        Assert.Null(traceParent);
        Assert.Null(traceState);
    }

    [Fact]
    public void Inject_EmitsOnlyW3cTraceFieldsWhenInnerPropagatorEmitsLegacyBaggage()
    {
        var propagator = new Cp6TraceContextDistributedPropagator(new LegacyCompatibleTestPropagator());
        using var activity = new Activity("outbound")
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
        activity.AddBaggage("unsafe", "secret-baggage");
        var carrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        propagator.Inject(
            activity,
            carrier,
            static (target, fieldName, fieldValue) =>
                ((IDictionary<string, string>)target!)[fieldName] = fieldValue);

        Assert.Contains("traceparent", carrier.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("baggage", carrier.Keys, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Correlation-Context", carrier.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void NullCallbacks_AreSafeAndProduceNoExtractedContext()
    {
        var propagator = new Cp6TraceContextDistributedPropagator(new LegacyCompatibleTestPropagator());

        propagator.Inject(null, null, null);
        propagator.ExtractTraceIdAndState(null, null, out var traceParent, out var traceState);

        Assert.Null(traceParent);
        Assert.Null(traceState);
        Assert.Null(propagator.ExtractBaggage(null, null));
    }

    private static void GetHeaderValues(
        object? target,
        string fieldName,
        out string? fieldValue,
        out IEnumerable<string>? fieldValues)
    {
        var carrier = (IReadOnlyDictionary<string, string[]>)target!;
        if (!carrier.TryGetValue(fieldName, out var values) || values.Length == 0)
        {
            fieldValue = null;
            fieldValues = null;
            return;
        }

        fieldValue = values.Length == 1 ? values[0] : null;
        fieldValues = values.Length > 1 ? values : null;
    }

    private sealed class LegacyCompatibleTestPropagator : DistributedContextPropagator
    {
        private static readonly string[] PropagationFields =
            ["traceparent", "Request-Id", "tracestate", "baggage", "Correlation-Context"];

        public override IReadOnlyCollection<string> Fields => PropagationFields;

        public override void Inject(
            Activity? activity,
            object? carrier,
            PropagatorSetterCallback? setter)
        {
            if (activity?.Id is null || setter is null)
            {
                return;
            }

            setter(carrier, "traceparent", activity.Id);
            setter(carrier, "Correlation-Context", "unsafe=secret-baggage");
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

            getter(carrier, "traceparent", out traceParent, out _);
            if (traceParent is null)
            {
                getter(carrier, "Request-Id", out traceParent, out _);
            }

            getter(carrier, "tracestate", out traceState, out _);
        }

        public override IEnumerable<KeyValuePair<string, string?>>? ExtractBaggage(
            object? carrier,
            PropagatorGetterCallback? getter) => null;
    }
}
