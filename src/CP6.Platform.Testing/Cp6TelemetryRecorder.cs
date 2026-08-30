using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using CP6.Platform.Abstractions;

namespace CP6.Platform.Testing;

public sealed class Cp6TelemetryRecorder : IDisposable
{
    private readonly HashSet<string> activitySourceNames;
    private readonly HashSet<string> meterNames;
    private readonly ConcurrentQueue<Cp6RecordedActivity> activities = [];
    private readonly ConcurrentQueue<Cp6RecordedMetric> metrics = [];
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;
    private long sequence;

    public Cp6TelemetryRecorder(
        IEnumerable<string>? activitySourceNames = null,
        IEnumerable<string>? meterNames = null)
    {
        this.activitySourceNames = Names(activitySourceNames ?? Cp6TelemetrySources.All, nameof(activitySourceNames));
        this.meterNames = Names(meterNames ?? Cp6TelemetryMeters.All, nameof(meterNames));
        activityListener = new ActivityListener
        {
            ShouldListenTo = source => this.activitySourceNames.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = RecordActivity
        };
        ActivitySource.AddActivityListener(activityListener);

        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (this.meterNames.Contains(instrument.Meter.Name))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        meterListener.SetMeasurementEventCallback<byte>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<short>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<int>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<float>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => RecordMetric(instrument, value, tags));
        meterListener.SetMeasurementEventCallback<decimal>((instrument, value, tags, _) => RecordMetric(instrument, (double)value, tags));
        meterListener.Start();
    }

    public IReadOnlyList<Cp6RecordedActivity> GetActivities() => activities
        .OrderBy(item => item.Sequence)
        .ToArray();

    public IReadOnlyList<Cp6RecordedMetric> GetMetrics() => metrics
        .OrderBy(item => item.Sequence)
        .ToArray();

    public ActivityTraceId AssertTraceTopology(params string[] operationNames)
    {
        ArgumentNullException.ThrowIfNull(operationNames);
        if (operationNames.Length == 0 || operationNames.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("At least one non-empty operation name is required.", nameof(operationNames));
        }

        var snapshot = GetActivities();
        var topology = operationNames.Select(operation =>
        {
            var matches = snapshot.Where(activity =>
                string.Equals(activity.OperationName, operation, StringComparison.Ordinal)).ToArray();
            return matches.Length == 1
                ? matches[0]
                : throw new InvalidOperationException(
                    $"Trace topology requires exactly one '{operation}' activity; found {matches.Length}.");
        }).ToArray();
        var traceId = topology[0].TraceId;
        if (traceId == default || topology.Any(activity => activity.TraceId != traceId))
        {
            throw new InvalidOperationException("Trace topology activities do not share one non-empty W3C Trace ID.");
        }

        if (topology.Select(activity => activity.SpanId).Distinct().Count() != topology.Length ||
            topology.Any(activity => activity.SpanId == default))
        {
            throw new InvalidOperationException("Trace topology requires distinct non-empty Span IDs.");
        }

        for (var index = 1; index < topology.Length; index++)
        {
            if (topology[index].ParentSpanId != topology[index - 1].SpanId)
            {
                throw new InvalidOperationException("Trace topology is not a direct parent-child chain.");
            }
        }

        return traceId;
    }

    public void EnsureOnlyAllowedTags(IReadOnlySet<string> allowedTagNames)
    {
        ArgumentNullException.ThrowIfNull(allowedTagNames);
        var rejected = GetActivities()
            .SelectMany(activity => activity.Tags.Keys)
            .Concat(GetMetrics().SelectMany(metric => metric.Tags.Keys))
            .Where(name => name.StartsWith("cp6.", StringComparison.Ordinal) && !allowedTagNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (rejected.Length > 0)
        {
            throw new InvalidOperationException(
                $"Recorded telemetry contains non-allowlisted CP6 tags: {string.Join(", ", rejected)}.");
        }
    }

    public void ThrowIfContainsForbiddenData(params string[] forbiddenTokens)
    {
        ArgumentNullException.ThrowIfNull(forbiddenTokens);
        var tokens = forbiddenTokens
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (tokens.Length != forbiddenTokens.Length)
        {
            throw new ArgumentException("Forbidden tokens must be non-empty and unique.", nameof(forbiddenTokens));
        }

        var values = GetActivities().SelectMany(activity =>
                new[] { activity.OperationName, activity.DisplayName }
                    .Concat(activity.Tags.SelectMany(TagText))
                    .Concat(activity.Baggage.SelectMany(TagText)))
            .Concat(GetMetrics().SelectMany(metric =>
                new[] { metric.MeterName, metric.InstrumentName }.Concat(metric.Tags.SelectMany(TagText))));
        foreach (var value in values)
        {
            if (tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("Recorded telemetry contains forbidden data.");
            }
        }
    }

    public void Dispose()
    {
        meterListener.Dispose();
        activityListener.Dispose();
    }

    private static HashSet<string> Names(IEnumerable<string> values, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(values);
        var names = values.ToArray();
        if (names.Any(string.IsNullOrWhiteSpace) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
        {
            throw new ArgumentException("Telemetry names must be non-empty and unique.", parameterName);
        }

        return names.ToHashSet(StringComparer.Ordinal);
    }

    private static IEnumerable<string> TagText(KeyValuePair<string, object?> tag)
    {
        yield return tag.Key;
        yield return Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private void RecordActivity(Activity activity)
    {
        activities.Enqueue(new Cp6RecordedActivity(
            Interlocked.Increment(ref sequence),
            activity.OperationName,
            activity.DisplayName,
            activity.Kind,
            activity.Status,
            activity.TraceId,
            activity.SpanId,
            activity.ParentSpanId,
            ImmutableTags(activity.TagObjects),
            ImmutableTags(activity.Baggage.Select(item =>
                new KeyValuePair<string, object?>(item.Key, item.Value)))));
    }

    private void RecordMetric<T>(
        Instrument instrument,
        T value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct, IConvertible
    {
        metrics.Enqueue(new Cp6RecordedMetric(
            Interlocked.Increment(ref sequence),
            instrument.Meter.Name,
            instrument.Name,
            value.ToDouble(CultureInfo.InvariantCulture),
            ImmutableTags(tags.ToArray())));
    }

    private static IImmutableDictionary<string, object?> ImmutableTags(
        IEnumerable<KeyValuePair<string, object?>> tags)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            builder[tag.Key] = tag.Value;
        }

        return builder.ToImmutable();
    }
}

public sealed record Cp6RecordedActivity(
    long Sequence,
    string OperationName,
    string DisplayName,
    ActivityKind Kind,
    ActivityStatusCode Status,
    ActivityTraceId TraceId,
    ActivitySpanId SpanId,
    ActivitySpanId ParentSpanId,
    IImmutableDictionary<string, object?> Tags,
    IImmutableDictionary<string, object?> Baggage);
