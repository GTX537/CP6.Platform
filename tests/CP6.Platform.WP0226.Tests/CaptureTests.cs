using System.Diagnostics;
using System.Diagnostics.Metrics;
using CP6.Platform.Testing.EvidenceV1;

namespace CP6.Platform.WP0226.Tests;

public class CaptureTests
{
    private static readonly DateTimeOffset Epoch = DateTimeOffset.Parse("2026-10-01T00:00:00Z");
    private static CaptureLimits Limits => new(100, 100000, TimeSpan.FromMinutes(1));
    private static KeyValuePair<string, object?>[] Tags(string id, string operation) => [new(EvidenceCase.CaseTag, id), new(EvidenceCase.OperationTag, operation)];
    private static Activity Start(ActivitySource source, string id, string operation, ActivityContext parent = default, params ActivityLink[] links) =>
        source.StartActivity(operation, ActivityKind.Internal, parent, Tags(id, operation), links)!;
    [Fact]
    public void Actual_fanout_retry_intermediate_Dapr_receive_and_apply_DAG_is_preserved()
    {
        using var source = new ActivitySource("wp0226.graph");
        var capture = new EvidenceCase("graph-case", Limits, new CaseClock(Epoch), [source.Name]);
        var expected = new[] { "root", "fanout", "retry-1", "retry-2", "dapr", "receive", "apply" };
        using (var root = Start(source, "graph-case", "root"))
        {
            using (Start(source, "graph-case", "fanout", root.Context)) { }
            using (Start(source, "graph-case", "retry-1", root.Context)) { }
            using var retry = Start(source, "graph-case", "retry-2", root.Context);
            using var dapr = Start(source, "graph-case", "dapr", retry.Context);
            using var receive = Start(source, "graph-case", "receive", dapr.Context);
            using (Start(source, "graph-case", "apply", receive.Context, new ActivityLink(dapr.Context))) { }
        }
        ExpectedEdge[] edges = [new("root", "fanout", EdgeKind.Parent), new("root", "retry-1", EdgeKind.Parent), new("root", "retry-2", EdgeKind.Parent),
            new("retry-2", "dapr", EdgeKind.Parent), new("dapr", "receive", EdgeKind.Parent), new("receive", "apply", EdgeKind.Parent),
            new("dapr", "apply", EdgeKind.Link), new("root", "apply", EdgeKind.Ancestor)];
        capture.Complete(expected, edges);
        capture.Dispose();
        var result = capture.Snapshot();
        Assert.Equal(CaptureState.Complete, result.State);
        Assert.Equal(7, result.Spans.Count);
        Assert.Single(result.Spans.Single(s => s.OperationKey == "apply").Links);
        Assert.True(result.Spans.Single(s => s.OperationKey == "apply").LocalSequence < result.Spans.Single(s => s.OperationKey == "root").LocalSequence); // stop sequence is explicitly not commit order.
        Assert.False(result.ProductionSloClaimed);
        Assert.NotEmpty(TraceGraph.Validate(result.Spans, edges.Where(e => e.FromOperation != "dapr")));
    }
    [Fact]
    public void Cross_case_and_dangling_edges_are_not_joined()
    {
        using var source = new ActivitySource("wp0226.cross");
        using var first = new EvidenceCase("a", Limits, new CaseClock(Epoch), [source.Name]);
        using var second = new EvidenceCase("b", Limits, new CaseClock(Epoch), [source.Name]);
        using (var parent = Start(source, "a", "parent"))
        using (Start(source, "b", "child", parent.Context, new ActivityLink(parent.Context))) { }
        Assert.Equal(CaptureState.Complete, first.Complete(["parent"]).State);
        var snapshot = second.Complete(["child"]);
        Assert.Equal(CaptureState.Incomplete, snapshot.State);
        Assert.Contains("DanglingParent", snapshot.Errors);
        Assert.Contains("DanglingLink", snapshot.Errors);
    }
    [Fact]
    public void Metric_listener_preserves_kinds_and_filters_concurrent_cases()
    {
        using var meter = new Meter("wp0226.metrics");
        using var a = new EvidenceCase("metric-a", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        using var b = new EvidenceCase("metric-b", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        meter.CreateHistogram<long>("large", "items").Record(9007199254740993L, Tags("metric-a", "long"));
        meter.CreateHistogram<decimal>("decimal", "items").Record(1.2300m, Tags("metric-a", "decimal"));
        meter.CreateHistogram<double>("binary", "items").Record(0.1d, Tags("metric-b", "double"));
        Assert.Equal(CaptureState.Complete, a.Complete(["long", "decimal"]).State);
        Assert.Equal(CaptureState.Complete, b.Complete(["double"]).State);
        ExactAssert.Equal("9007199254740993", a.Snapshot().Values[0].Value.OriginalLexical);
        Assert.Equal("System.Decimal", a.Snapshot().Values[1].Value.ClrKind);
        Assert.Single(b.Snapshot().Values);
    }
    [Theory]
    [InlineData("count")][InlineData("bytes")][InlineData("time")][InlineData("duplicate")][InlineData("cross")][InlineData("observer")][InlineData("cleanup")][InlineData("dispose")]
    public void Limits_and_lifecycle_failures_never_produce_complete(string failure)
    {
        var clock = new CaseClock(Epoch);
        var limits = failure == "bytes" ? Limits with { MaximumBytes = 1 } : Limits with { MaximumRecords = 1 };
        var capture = new EvidenceCase("limits", limits, clock,
            observer: failure == "observer" ? () => throw new InvalidOperationException() : null,
            cleanup: failure == "cleanup" ? () => throw new InvalidOperationException() : null);
        if (failure == "time") clock.Advance(TimeSpan.FromMinutes(2));
        capture.RecordValue(failure == "cross" ? "other" : "limits", "one", ExactValue.FromClr(1L));
        if (failure is "count" or "duplicate") capture.RecordValue("limits", failure == "duplicate" ? "one" : "two", ExactValue.FromClr(2L));
        if (failure != "dispose") capture.Complete(["one"]);
        capture.Dispose();
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        Assert.NotEmpty(capture.Snapshot().Errors);
    }
    [Fact]
    public void Dispose_with_inflight_span_cannot_claim_complete_or_capture_afterwards()
    {
        using var source = new ActivitySource("wp0226.dispose");
        var capture = new EvidenceCase("dispose", Limits, new CaseClock(Epoch), [source.Name]);
        var activity = Start(source, "dispose", "inflight");
        capture.Complete(["inflight"]);
        capture.Dispose();
        activity.Dispose();
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        Assert.Empty(capture.Snapshot().Spans);
    }
    [Fact]
    public void Replayed_operation_and_wrong_expected_inventory_are_rejected()
    {
        using var capture = new EvidenceCase("inventory", Limits, new CaseClock(Epoch));
        capture.RecordValue("inventory", "native-op-1", ExactValue.FromClr(1));
        Assert.Equal(CaptureState.Incomplete, capture.Complete(["native-op-1", "native-op-2"]).State);
    }

    [Fact]
    public void Elapsed_capture_is_incomplete_even_without_a_later_record()
    {
        var clock = new CaseClock(Epoch);
        using var capture = new EvidenceCase("expired", Limits, clock);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
    }

    [Fact]
    public void Link_capture_keeps_trace_state_flags_and_link_tags()
    {
        using var source = new ActivitySource("wp0226.linkmetadata");
        using var capture = new EvidenceCase("links", Limits, new CaseClock(Epoch), [source.Name]);
        var context = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, "vendor=state");
        using (Start(source, "links", "operation", default, new ActivityLink(context, new ActivityTagsCollection { { "link.kind", "replay" } }))) { }
        var serialized = System.Text.Json.JsonSerializer.Serialize(capture.Snapshot().Spans.Single().Links.Single());
        Assert.Contains("vendor=state", serialized, StringComparison.Ordinal);
        Assert.Contains("replay", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_case_tags_cannot_contaminate_either_case()
    {
        using var meter = new Meter("wp0226.ambiguous");
        using var a = new EvidenceCase("a", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        using var b = new EvidenceCase("b", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        meter.CreateCounter<int>("ambiguous").Add(1,
            new KeyValuePair<string, object?>(EvidenceCase.CaseTag, "a"),
            new KeyValuePair<string, object?>(EvidenceCase.CaseTag, "b"),
            new KeyValuePair<string, object?>(EvidenceCase.OperationTag, "op"));
        Assert.Empty(a.Snapshot().Values);
        Assert.Empty(b.Snapshot().Values);
        Assert.Equal(CaptureState.Incomplete, a.Complete(["op"]).State);
        Assert.Equal(CaptureState.Incomplete, b.Complete(["op"]).State);
    }

    [Fact]
    public void Inflight_count_is_bounded_and_cleanup_is_included_before_returning_complete()
    {
        using var source = new ActivitySource("wp0226.inflight-limit");
        using var capture = new EvidenceCase("bounded", Limits with { MaximumRecords = 1 }, new CaseClock(Epoch), [source.Name]);
        using var first = Start(source, "bounded", "first");
        using var second = Start(source, "bounded", "second");
        Assert.Equal(1, capture.Snapshot().InFlight);
        Assert.Contains("InFlightOverflow", capture.Snapshot().Errors);
        using var cleanup = new EvidenceCase("cleanup-final", Limits, new CaseClock(Epoch), cleanup: () => throw new IOException());
        cleanup.RecordValue("cleanup-final", "one", ExactValue.FromClr(1));
        Assert.Equal(CaptureState.Incomplete, cleanup.Complete(["one"]).State);
    }

    [Fact]
    public void Graph_rejects_cycles_duplicate_span_ids_and_missing_ancestor_edges()
    {
        var tags = new Dictionary<string, string?>();
        var trace = new string('a', 32);
        var a = new CapturedSpan(1, "a", "a", trace, new('1', 16), new('2', 16), [], tags);
        var b = new CapturedSpan(2, "b", "b", trace, new('2', 16), new('1', 16), [], tags);
        Assert.Contains("CyclicGraph", TraceGraph.Validate([a, b], [new("a", "b", EdgeKind.Parent), new("b", "a", EdgeKind.Parent)]));
        Assert.Contains("DuplicateOrInvalidSpan", TraceGraph.Validate([a, a], []));
        Assert.Contains("MissingExpectedEdge", TraceGraph.Validate([a with { ParentSpanId = new('0', 16) }], [new("a", "missing", EdgeKind.Ancestor)]));
    }

    [Fact]
    public async Task Concurrent_cases_capture_only_their_own_metrics()
    {
        using var meter = new Meter("wp0226.concurrent-cases");
        var counter = meter.CreateCounter<long>("concurrent");
        using var a = new EvidenceCase("case-a", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        using var b = new EvidenceCase("case-b", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        await Task.WhenAll(Enumerable.Range(0, 32).Select(i => Task.Run(() => counter.Add(i, Tags(i % 2 == 0 ? "case-a" : "case-b", $"op-{i}")))));
        var aResult = a.Complete(Enumerable.Range(0, 16).Select(i => $"op-{i * 2}"));
        var bResult = b.Complete(Enumerable.Range(0, 16).Select(i => $"op-{i * 2 + 1}"));
        Assert.Equal(CaptureState.Complete, aResult.State);
        Assert.Equal(CaptureState.Complete, bResult.State);
        Assert.Equal(16, aResult.Values.Count);
        Assert.Equal(16, bResult.Values.Count);
    }

    [Fact]
    public void Listener_source_inventory_is_bounded_before_materializing_input()
    {
        Assert.Throws<ArgumentException>(() => new EvidenceCase("source-limit", Limits, new CaseClock(Epoch), Enumerable.Range(0, 129).Select(i => $"source-{i}")));
    }

    [Fact]
    public void Long_intermediate_chain_is_validated_without_recursive_stack_or_quadratic_cycle_scan()
    {
        const int count = 10000;
        var tags = new Dictionary<string, string?>();
        var spans = Enumerable.Range(1, count).Select(i => new CapturedSpan(i, $"op-{i}", "intermediate", new('a', 32), i.ToString("x16"), (i - 1).ToString("x16"), [], tags)).ToArray();
        var edges = Enumerable.Range(2, count - 1).Select(i => new ExpectedEdge($"op-{i - 1}", $"op-{i}", EdgeKind.Parent)).Append(new("op-1", $"op-{count}", EdgeKind.Ancestor));
        Assert.Empty(TraceGraph.Validate(spans, edges));
    }

    [Fact]
    public void Actual_metric_tag_inventory_is_limited_before_copying()
    {
        using var meter = new Meter("wp0226.metric-tag-limit");
        using var capture = new EvidenceCase("tag-limit", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        var tags = Tags("tag-limit", "metric").Concat(Enumerable.Range(0, 129).Select(i => new KeyValuePair<string, object?>($"tag-{i}", "x"))).ToArray();
        meter.CreateCounter<int>("metric").Add(1, tags);
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        Assert.Empty(capture.Snapshot().Values);
    }

    [Fact]
    public void Actual_activity_tag_inventory_is_limited_before_copying()
    {
        using var source = new ActivitySource("wp0226.activity-tag-limit");
        using var capture = new EvidenceCase("tag-limit", Limits, new CaseClock(Epoch), [source.Name]);
        using (var activity = Start(source, "tag-limit", "span"))
            for (var i = 0; i < 129; i++) activity.SetTag($"tag-{i}", "x");
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        Assert.Empty(capture.Snapshot().Spans);
    }

    [Fact]
    public void Graph_actual_link_enumeration_has_budget_even_for_duplicate_links()
    {
        var tags = new Dictionary<string, string?>();
        var trace = new string('a', 32);
        var a = new CapturedSpan(1, "a", "a", trace, new('1', 16), new('0', 16), [], tags);
        var b = new CapturedSpan(2, "b", "b", trace, new('2', 16), new('0', 16), Array.AsReadOnly(Enumerable.Repeat(new SpanReference(trace, a.SpanId), 100001).ToArray()), tags);
        Assert.Contains("GraphResourceLimit", TraceGraph.Validate([a, b], [new("a", "b", EdgeKind.Link)]));
    }

    [Fact]
    public void Large_activity_tag_is_rejected_before_serialization_allocation()
    {
        using var source = new ActivitySource("wp0226.large-tag");
        using var capture = new EvidenceCase("large-tag", Limits, new CaseClock(Epoch), [source.Name]);
        var activity = Start(source, "large-tag", "span");
        activity.SetTag("large", new string('x', 1000000));
        var before = GC.GetAllocatedBytesForCurrentThread();
        activity.Dispose();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(allocated < 100000, $"Capture allocated {allocated} bytes for an oversized tag.");
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
    }

    [Fact]
    public void Metric_tags_are_retained_and_arbitrary_tag_code_is_not_executed()
    {
        using var meter = new Meter("wp0226.retained-tags");
        using var capture = new EvidenceCase("retained", Limits, new CaseClock(Epoch), meters: [meter.Name]);
        meter.CreateCounter<int>("metric").Add(1, Tags("retained", "one").Append(new("fixture.tag", "retained-text")).ToArray());
        Assert.Equal("retained-text", capture.Snapshot().Values.Single().Tags!["fixture.tag"]);
        var custom = new CustomTag();
        meter.CreateCounter<int>("other").Add(1, Tags("retained", "two").Append(new("custom", custom)).ToArray());
        Assert.False(custom.Invoked);
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
    }
    private sealed class CustomTag
    {
        public bool Invoked { get; private set; }
        public override string ToString() { Invoked = true; return "custom"; }
    }

    [Fact]
    public void Expanded_exact_numeric_backing_is_charged_to_capture_byte_budget()
    {
        using var capture = new EvidenceCase("numeric-budget", Limits with { MaximumBytes = 512 }, new CaseClock(Epoch));
        capture.RecordValue("numeric-budget", "large", ExactValue.FromUtf8(System.Text.Encoding.UTF8.GetBytes("1e16384")));
        Assert.Equal(CaptureState.Incomplete, capture.Complete(["large"]).State);
        Assert.Empty(capture.Snapshot().Values);
    }

    [Fact]
    public void Small_exact_numeric_value_remains_accepted_with_small_byte_budget()
    {
        using var capture = new EvidenceCase("numeric-small", Limits with { MaximumBytes = 512 }, new CaseClock(Epoch));
        capture.RecordValue("numeric-small", "small", ExactValue.FromUtf8(System.Text.Encoding.UTF8.GetBytes("1.25")));
        Assert.Equal(CaptureState.Complete, capture.Complete(["small"]).State);
        Assert.Single(capture.Snapshot().Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Throwing_completion_inventory_cannot_be_disposed_into_complete(bool graphThrows)
    {
        var cleanupCount = 0;
        using var capture = new EvidenceCase("completion-throw", Limits, new CaseClock(Epoch), cleanup: () => cleanupCount++);
        capture.RecordValue("completion-throw", "one", ExactValue.FromClr(1));
        try { capture.Complete(graphThrows ? ["one"] : ThrowingKeys(), graphThrows ? ThrowingEdges() : []); }
        catch (InvalidOperationException) { /* Preserve observation of the pre-fix exception path. */ }
        var cleanedByComplete = cleanupCount == 1;
        capture.Dispose();
        Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        Assert.True(cleanedByComplete);
        Assert.Equal(1, cleanupCount);
        Assert.Contains("CompletionValidationFailure", capture.Snapshot().Errors);
    }

    private static IEnumerable<string> ThrowingKeys()
    { yield return "one"; throw new InvalidOperationException("Synthetic expected-key enumeration failure."); }
    private static IEnumerable<ExpectedEdge> ThrowingEdges()
    { yield return new("one", "two", EdgeKind.Parent); throw new InvalidOperationException("Synthetic expected-graph enumeration failure."); }

    [Fact]
    public void Cleanup_reentrant_capture_snapshot_cannot_publish_premature_complete()
    {
        EvidenceCase? capture = null;
        CaptureSnapshot? duringCleanup = null;
        capture = new EvidenceCase("cleanup-reentry", Limits, new CaseClock(Epoch), cleanup: () =>
        {
            duringCleanup = capture!.Snapshot();
            throw new IOException("Synthetic cleanup failure after reentrant snapshot.");
        });
        using (capture)
        {
            capture.RecordValue("cleanup-reentry", "one", ExactValue.FromClr(1));
            Assert.Equal(CaptureState.Incomplete, capture.Complete(["one"]).State);
            Assert.NotNull(duringCleanup);
            Assert.NotEqual(CaptureState.Complete, duringCleanup.State);
            Assert.Contains("CleanupFailure", capture.Snapshot().Errors);
        }
    }

    [Fact]
    public void Observer_cannot_complete_capture_while_its_callback_is_active()
    {
        EvidenceCase? capture = null;
        CaptureSnapshot? premature = null;
        capture = new EvidenceCase("observer-reentry", Limits, new CaseClock(Epoch), observer: () =>
        {
            premature = capture!.Complete(["one"]);
            throw new IOException("Synthetic observer failure after attempted completion.");
        });
        using (capture)
        {
            capture.RecordValue("observer-reentry", "one", ExactValue.FromClr(1));
            Assert.NotNull(premature);
            Assert.NotEqual(CaptureState.Complete, premature.State);
            Assert.Equal(CaptureState.Incomplete, capture.Snapshot().State);
        }
    }
}
