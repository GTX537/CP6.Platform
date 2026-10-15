using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Collections.ObjectModel;
using System.Globalization;

namespace CP6.Platform.Testing.EvidenceV1;

public enum CaptureState { Capturing, Complete, Incomplete }
public sealed record CaptureLimits(int MaximumRecords, long MaximumBytes, TimeSpan MaximumDuration);
public sealed record CapturedValue(long LocalSequence, string OperationKey, string Instrument, string Unit, ExactValue Value,
    IReadOnlyDictionary<string, string?>? Tags = null);
public sealed record CapturedSpan(long LocalSequence, string OperationKey, string Name, string TraceId, string SpanId,
    string ParentSpanId, IReadOnlyList<SpanReference> Links, IReadOnlyDictionary<string, string?> Tags);
public sealed record SpanReference(string TraceId, string SpanId, string? TraceState = null,
    ActivityTraceFlags TraceFlags = ActivityTraceFlags.None, IReadOnlyDictionary<string, string?>? Tags = null);
public enum EdgeKind { Parent, Link, Ancestor }
public sealed record ExpectedEdge(string FromOperation, string ToOperation, EdgeKind Kind);
public sealed record CaptureSnapshot(string CaseId, CaptureState State, IReadOnlyList<CapturedValue> Values,
    IReadOnlyList<CapturedSpan> Spans, IReadOnlyList<string> Errors, long Bytes, int InFlight)
{
    public bool ProductionSloClaimed => false;
    public string SequenceMeaning => "local-observation-order-only-not-database-commit-order";
}

/// <summary>Opt-in case-scoped listener. Every record requires explicit case and unique operation tags.</summary>
public sealed class EvidenceCase : IDisposable
{
    public const string CaseTag = "cp6.case.id";
    public const string OperationTag = "cp6.operation.key";
    private readonly object gate = new();
    private readonly CaptureLimits limits;
    private readonly TimeProvider time;
    private readonly long started;
    private readonly List<CapturedValue> values = [];
    private readonly List<CapturedSpan> spans = [];
    private readonly HashSet<string> operations = new(StringComparer.Ordinal);
    private readonly HashSet<string> inFlight = new(StringComparer.Ordinal);
    private readonly HashSet<string> errors = new(StringComparer.Ordinal);
    private readonly ActivityListener activityListener;
    private readonly MeterListener meterListener;
    private readonly Action? observer;
    private readonly Action? cleanup;
    private long sequence;
    private long bytes;
    private bool sealedCase;
    private bool disposed;
    private bool completionValidated;
    private bool cleanupFinished;
    private int activeObservers;
    public EvidenceCase(string caseId, CaptureLimits limits, TimeProvider timeProvider,
        IEnumerable<string>? activitySources = null, IEnumerable<string>? meters = null, Action? observer = null, Action? cleanup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        if (caseId.Length > 256) throw new ArgumentException("Case identity exceeds resource bound.", nameof(caseId));
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (limits.MaximumRecords is < 1 or > 100000 || limits.MaximumBytes is < 1 or > 67108864 || limits.MaximumDuration <= TimeSpan.Zero || limits.MaximumDuration > TimeSpan.FromHours(24))
            throw new ArgumentOutOfRangeException(nameof(limits));
        CaseId = caseId; this.limits = limits; time = timeProvider; started = time.GetTimestamp(); this.observer = observer; this.cleanup = cleanup;
        var sources = SourceNames(activitySources ?? []);
        var meterNames = SourceNames(meters ?? []);
        activityListener = new ActivityListener
        {
            ShouldListenTo = source => sources.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => Matches(options.Tags) ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            SampleUsingParentId = (ref ActivityCreationOptions<string> options) => Matches(options.Tags) ? ActivitySamplingResult.AllDataAndRecorded : ActivitySamplingResult.None,
            ActivityStarted = activity => { lock (gate) { if (Matches(activity.TagObjects)) { if (sealedCase || disposed) Fail("ActivityAfterClose"); else if (inFlight.Count >= limits.MaximumRecords) Fail("InFlightOverflow"); else if (errors.Count == 0) inFlight.Add(activity.Id!); } } },
            ActivityStopped = OnActivity
        };
        ActivitySource.AddActivityListener(activityListener);
        meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) => { if (meterNames.Contains(instrument.Meter.Name)) listener.EnableMeasurementEvents(instrument); }
        };
        meterListener.SetMeasurementEventCallback<byte>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<short>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<int>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<long>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<float>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<double>((i, v, t, _) => Metric(i, v, t));
        meterListener.SetMeasurementEventCallback<decimal>((i, v, t, _) => Metric(i, v, t));
        meterListener.Start();
    }
    public string CaseId { get; }
    private static HashSet<string> SourceNames(IEnumerable<string> names)
    {
        var items = names.Take(129).ToArray();
        if (items.Length > 128 || items.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 256) || items.Distinct(StringComparer.Ordinal).Count() != items.Length)
            throw new ArgumentException("At most 128 unique source names of at most 256 characters are supported.");
        return items.ToHashSet(StringComparer.Ordinal);
    }
    private bool Matches(IEnumerable<KeyValuePair<string, object?>>? tags)
    {
        if (tags is null) return false;
        var count = 0; var identities = 0; var matches = false;
        try
        {
            foreach (var tag in tags)
            {
                if (++count > 128) { if (matches) lock (gate) Fail("TagLimit"); return false; }
                if (tag.Key == CaseTag) { identities++; matches |= tag.Value is string id && id == CaseId; }
            }
            if (matches && identities != 1) { lock (gate) Fail("AmbiguousCaseTags"); }
            return matches && identities == 1;
        }
        catch (Exception) { lock (gate) Fail("ScopeCaptureFailure"); return false; }
    }
    // Conservative JSON/UTF-8 upper bound, including worst-case escaping, before any snapshot serialization.
    private static long TextBudget(string? value) => value is null ? 4 : checked(6L * value.Length + 2);
    private static void Charge(ref long remaining, long size)
    { if (size > remaining) throw new InvalidOperationException("Capture byte budget exceeded."); remaining -= size; }
    private static IReadOnlyDictionary<string, string?> CaptureTags(IEnumerable<KeyValuePair<string, object?>> input, ref long remaining)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        var count = 0;
        foreach (var tag in input)
        {
            if (++count > 128 || tag.Key is null) throw new InvalidOperationException("Tag inventory limit exceeded.");
            // Never execute arbitrary object.ToString() in an observer callback.
            if (tag.Value is not (null or string or bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal or Guid or DateTime or DateTimeOffset))
                throw new InvalidOperationException("Unsupported telemetry tag kind.");
            var value = tag.Value is string text ? text : Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
            Charge(ref remaining, TextBudget(tag.Key) + TextBudget(value) + 64);
            if (!result.TryAdd(tag.Key, value)) throw new InvalidOperationException("Duplicate telemetry tag.");
        }
        return new ReadOnlyDictionary<string, string?>(result);
    }
    public void RecordValue(string caseId, string operationKey, ExactValue value, string instrument = "explicit", string unit = "1")
    {
        ArgumentNullException.ThrowIfNull(value);
        lock (gate)
        {
            if (caseId != CaseId) { Fail("CrossCaseRecord"); return; }
            RecordValueCore(operationKey, value, instrument, unit, null, 0);
        }
    }
    private void RecordValueCore(string operation, ExactValue value, string instrument, string unit, IReadOnlyDictionary<string, string?>? tags, long tagBytes)
    {
        var size = checked((long)value.ByteLength + value.NumericBackingBytes + TextBudget(value.OriginalLexical) + TextBudget(operation) + TextBudget(instrument) + TextBudget(unit) + tagBytes + 128);
        if (!Admit(operation, size)) return;
        values.Add(new(++sequence, operation, instrument, unit, value, tags));
        Observe();
    }
    private void Metric<T>(Instrument instrument, T value, ReadOnlySpan<KeyValuePair<string, object?>> tags) where T : struct
    {
        try
        {
            var matches = false; var identities = 0; string? operation = null;
            for (var i = 0; i < Math.Min(tags.Length, 129); i++)
            {
                if (tags[i].Key == CaseTag) { identities++; matches |= tags[i].Value is string id && id == CaseId; }
                if (tags[i].Key == OperationTag) operation = tags[i].Value as string;
            }
            if (!matches) return;
            lock (gate)
            {
                if (tags.Length > 128 || identities != 1) { Fail("TagLimit"); return; }
                var remaining = limits.MaximumBytes - bytes;
                var initial = remaining;
                var capturedTags = CaptureTags(tags.ToArray(), ref remaining); // At most 128 pairs, prechecked above.
                if (operation is null) { Fail("MissingOperationKey"); return; }
                RecordValueCore(operation, ExactValue.FromClr(value), instrument.Name, instrument.Unit ?? "1", capturedTags, initial - remaining);
            }
        }
        catch (Exception) { lock (gate) Fail("MetricCaptureFailure"); }
    }
    private void OnActivity(Activity activity)
    {
        lock (gate)
        {
            if (!inFlight.Remove(activity.Id!)) return;
            try
            {
                if (!Matches(activity.TagObjects)) { Fail("CrossCaseMutation"); return; }
                var operation = activity.GetTagItem(OperationTag) as string;
                if (operation is null) { Fail("MissingOperationKey"); return; }
                var remaining = limits.MaximumBytes - bytes;
                var initial = remaining;
                Charge(ref remaining, TextBudget(operation) + TextBudget(activity.OperationName) + 256);
                var tags = CaptureTags(activity.TagObjects, ref remaining);
                var links = new List<SpanReference>();
                foreach (var link in activity.Links)
                {
                    if (links.Count >= 128) throw new InvalidOperationException("Activity link inventory limit exceeded.");
                    Charge(ref remaining, TextBudget(link.Context.TraceState) + 256);
                    var linkTags = CaptureTags(link.Tags ?? [], ref remaining);
                    links.Add(new(link.Context.TraceId.ToHexString(), link.Context.SpanId.ToHexString(), link.Context.TraceState, link.Context.TraceFlags, linkTags));
                }
                var captured = new CapturedSpan(sequence + 1, operation, activity.OperationName, activity.TraceId.ToHexString(), activity.SpanId.ToHexString(),
                    activity.ParentSpanId.ToHexString(), Array.AsReadOnly(links.ToArray()), tags);
                if (!Admit(operation, initial - remaining)) return;
                sequence++; spans.Add(captured); Observe();
            }
            catch (Exception) { Fail("ActivityCaptureFailure"); }
        }
    }
    private bool Admit(string operation, long size)
    {
        if (disposed || sealedCase) { Fail("RecordAfterClose"); return false; }
        if (time.GetElapsedTime(started) > limits.MaximumDuration) { Fail("TimeLimit"); return false; }
        if (errors.Count != 0) return false;
        if (values.Count + spans.Count >= limits.MaximumRecords || size > limits.MaximumBytes - bytes) { Fail("CaptureOverflow"); return false; }
        if (string.IsNullOrWhiteSpace(operation) || !operations.Add(operation)) { Fail("DuplicateOrMissingOperation"); return false; }
        bytes += size; return true;
    }
    private void Observe()
    {
        activeObservers++;
        try { observer?.Invoke(); }
        catch (Exception) { Fail("ObserverFailure"); }
        finally { activeObservers--; }
    }
    private void Fail(string reason) => errors.Add(reason);
    public CaptureSnapshot Complete(IEnumerable<string> expectedOperationKeys, IEnumerable<ExpectedEdge>? graph = null)
    {
        ArgumentNullException.ThrowIfNull(expectedOperationKeys);
        lock (gate)
        {
            if (disposed || sealedCase) { Fail("CompletionAfterClose"); return Snapshot(); }
            sealedCase = true;
            try
            {
                if (activeObservers != 0) Fail("CompletionDuringObserver");
                else
                {
                    if (time.GetElapsedTime(started) > limits.MaximumDuration) Fail("TimeLimit");
                    if (inFlight.Count != 0) Fail("InFlightAtCompletion");
                    var expected = expectedOperationKeys.Take(limits.MaximumRecords + 1).ToArray();
                    var actual = values.Select(x => x.OperationKey).Concat(spans.Select(x => x.OperationKey)).ToHashSet(StringComparer.Ordinal);
                    if (expected.Length == 0 || expected.Distinct(StringComparer.Ordinal).Count() != expected.Length || !actual.SetEquals(expected)) Fail("InventoryMismatch");
                    foreach (var error in TraceGraph.Validate(spans, graph ?? [])) Fail(error);
                    completionValidated = true;
                }
            }
            catch (Exception) { Fail("CompletionValidationFailure"); }
            finally { Dispose(); } // Validation failures also shut listeners down and execute cleanup.
            return Snapshot();
        }
    }
    public CaptureSnapshot Snapshot()
    {
        lock (gate)
        {
            if (!disposed && time.GetElapsedTime(started) > limits.MaximumDuration) Fail("TimeLimit");
            return new(CaseId, errors.Count == 0 && completionValidated && cleanupFinished && activeObservers == 0 ? CaptureState.Complete : errors.Count == 0 && !disposed ? CaptureState.Capturing : CaptureState.Incomplete,
                Array.AsReadOnly(values.ToArray()), Array.AsReadOnly(spans.ToArray()), Array.AsReadOnly(errors.Order(StringComparer.Ordinal).ToArray()), bytes, inFlight.Count);
        }
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (!sealedCase || inFlight.Count != 0) Fail("DisposedBeforeCompletion");
            try { meterListener.Dispose(); } catch (Exception) { Fail("ListenerCleanupFailure"); }
            try { activityListener.Dispose(); } catch (Exception) { Fail("ListenerCleanupFailure"); }
            try { cleanup?.Invoke(); } catch (Exception) { Fail("CleanupFailure"); }
            cleanupFinished = true;
        }
    }
}

public static class TraceGraph
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<CapturedSpan> spans, IEnumerable<ExpectedEdge> expectedEdges)
    {
        ArgumentNullException.ThrowIfNull(spans);
        ArgumentNullException.ThrowIfNull(expectedEdges);
        if (spans.Count > 100000) return ["GraphResourceLimit"];
        var errors = new HashSet<string>(StringComparer.Ordinal);
        var byIdentity = new Dictionary<(string, string), CapturedSpan>();
        var byOperation = new Dictionary<string, CapturedSpan>(StringComparer.Ordinal);
        foreach (var span in spans)
        {
            if (span.TraceId == new string('0', 32) || span.SpanId == new string('0', 16) || !byIdentity.TryAdd((span.TraceId, span.SpanId), span) || !byOperation.TryAdd(span.OperationKey, span)) errors.Add("DuplicateOrInvalidSpan");
        }
        var actual = new HashSet<ExpectedEdge>();
        var directEdgeCount = 0;
        foreach (var span in spans)
        {
            if (span.ParentSpanId != new string('0', 16))
            {
                if (++directEdgeCount > 100000) return ["GraphResourceLimit"];
                if (byIdentity.TryGetValue((span.TraceId, span.ParentSpanId), out var parent)) actual.Add(new(parent.OperationKey, span.OperationKey, EdgeKind.Parent));
                else errors.Add("DanglingParent");
            }
            foreach (var link in span.Links)
            {
                if (++directEdgeCount > 100000) return ["GraphResourceLimit"];
                if (byIdentity.TryGetValue((link.TraceId, link.SpanId), out var linked)) actual.Add(new(linked.OperationKey, span.OperationKey, EdgeKind.Link));
                else errors.Add("DanglingLink");
            }
        }
        var expected = expectedEdges.Take(100001).ToArray();
        if (expected.Length > 100000) return ["GraphResourceLimit"];
        if (expected.Distinct().Count() != expected.Length) errors.Add("DuplicateExpectedEdge");
        var direct = expected.Where(x => x.Kind != EdgeKind.Ancestor).ToHashSet();
        if (!direct.SetEquals(actual)) errors.Add("GraphEdgeMismatch");
        var adjacency = actual.GroupBy(x => x.FromOperation).ToDictionary(g => g.Key, g => g.Select(x => x.ToOperation).ToArray(), StringComparer.Ordinal);
        var traversalBudget = 10000000;
        bool Reaches(string from, string to)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(from);
            while (pending.TryPop(out var current))
            {
                if (!visited.Add(current) || !adjacency.TryGetValue(current, out var children)) continue;
                foreach (var child in children)
                {
                    if (--traversalBudget < 0) { errors.Add("GraphResourceLimit"); return false; }
                    if (child == to) return true;
                    pending.Push(child);
                }
            }
            return false;
        }
        // Kahn's algorithm bounds cycle detection to vertices plus edges, without recursion.
        var indegree = byOperation.Keys.ToDictionary(key => key, _ => 0, StringComparer.Ordinal);
        foreach (var edge in actual) if (indegree.ContainsKey(edge.ToOperation)) indegree[edge.ToOperation]++;
        var roots = new Queue<string>(indegree.Where(item => item.Value == 0).Select(item => item.Key));
        var removed = 0;
        while (roots.TryDequeue(out var operation))
        {
            removed++;
            if (!adjacency.TryGetValue(operation, out var children)) continue;
            foreach (var child in children) if (indegree.ContainsKey(child) && --indegree[child] == 0) roots.Enqueue(child);
        }
        if (removed != byOperation.Count) errors.Add("CyclicGraph");
        foreach (var edge in expected)
            if (!byOperation.ContainsKey(edge.FromOperation) || !byOperation.ContainsKey(edge.ToOperation) ||
                (edge.Kind == EdgeKind.Ancestor && (traversalBudget < 0 || !Reaches(edge.FromOperation, edge.ToOperation)))) errors.Add("MissingExpectedEdge");
        return errors.Order(StringComparer.Ordinal).ToArray();
    }
}
