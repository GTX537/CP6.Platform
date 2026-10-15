using System.Net;
using CP6.Platform.Testing;
using CP6.Platform.Testing.EvidenceV1;

namespace CP6.Platform.WP0226.Tests;

public class FaultTests
{
    private static DeterministicFaultCase Fixture(Cp6HttpFaultOutcome[] outcomes, RetryPlacement placement = RetryPlacement.RetryOutsideFault,
        int retries = 0, HttpStatusCode[]? terminal = null, Action? observer = null, Action? cleanup = null) => new("fault-case", "in-memory:wp0226",
            outcomes, terminal ?? [HttpStatusCode.OK], placement, retries, 10, 10,
            DateTimeOffset.Parse("2026-10-01T00:00:00Z"), 123, observer, cleanup);
    [Fact]
    public async Task Status_and_throw_never_reach_terminal()
    {
        using var status = Fixture([Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable)]);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await status.SendAsync());
        Assert.Equal(CaptureState.Complete, status.Complete(1, 0).State);
        using var thrown = Fixture([Cp6HttpFaultOutcome.Throw(new IOException("scripted"))]);
        await Assert.ThrowsAsync<IOException>(() => thrown.SendAsync());
        Assert.Equal(0, thrown.Snapshot().TerminalCalls);
        Assert.Equal(CaptureState.Incomplete, thrown.Complete(1, 0).State);
    }
    [Fact]
    public async Task Success_and_delay_reach_only_named_terminal_with_deterministic_clock_and_random()
    {
        using var a = Fixture([Cp6HttpFaultOutcome.Success, Cp6HttpFaultOutcome.Delay(TimeSpan.FromSeconds(2))], terminal: [HttpStatusCode.OK, HttpStatusCode.Accepted]);
        using var b = Fixture([Cp6HttpFaultOutcome.Success]);
        Assert.Equal(a.Random.Next(), b.Random.Next());
        var start = a.Clock.GetUtcNow();
        Assert.Equal(HttpStatusCode.OK, await a.SendAsync());
        Assert.Equal(HttpStatusCode.Accepted, await a.SendAsync());
        Assert.Equal(TimeSpan.FromSeconds(2), a.Clock.GetUtcNow() - start);
        Assert.Equal(CaptureState.Complete, a.Complete(2, 2).State);
    }
    [Fact]
    public async Task Retry_placement_explicitly_changes_script_and_terminal_counts()
    {
        using var outer = Fixture([Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable), Cp6HttpFaultOutcome.Success], retries: 1);
        Assert.Equal(HttpStatusCode.OK, await outer.SendAsync());
        Assert.Equal(CaptureState.Complete, outer.Complete(2, 1).State);
        using var inner = Fixture([Cp6HttpFaultOutcome.Success], RetryPlacement.RetryInsideFault, 1, [HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK]);
        Assert.Equal(HttpStatusCode.OK, await inner.SendAsync());
        Assert.Equal(CaptureState.Complete, inner.Complete(1, 2).State);
        using var blocked = Fixture([Cp6HttpFaultOutcome.Status(HttpStatusCode.ServiceUnavailable)], RetryPlacement.RetryInsideFault, 1);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await blocked.SendAsync());
        Assert.Equal(0, blocked.Snapshot().TerminalCalls);
    }
    [Fact]
    public async Task Exhaustion_cancellation_observer_and_cleanup_failures_are_visible()
    {
        using var exhausted = Fixture([Cp6HttpFaultOutcome.Success]);
        await exhausted.SendAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => exhausted.SendAsync());
        Assert.Contains("ScriptExhausted", exhausted.Snapshot().Errors);
        using var cancelled = Fixture([Cp6HttpFaultOutcome.Success]);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.SendAsync(new CancellationToken(true)));
        Assert.Equal(0, cancelled.Snapshot().TerminalCalls);
        var observed = Fixture([Cp6HttpFaultOutcome.Success], observer: () => throw new IOException(), cleanup: () => throw new IOException());
        Assert.Equal(HttpStatusCode.OK, await observed.SendAsync()); // observer is evidence-only.
        observed.Complete(1, 1); observed.Dispose();
        Assert.Equal(CaptureState.Incomplete, observed.Snapshot().State);
        Assert.Contains("ObserverFailure", observed.Snapshot().Errors);
        Assert.Contains("CleanupFailure", observed.Snapshot().Errors);
    }
    [Fact]
    public async Task Cancellation_after_script_observer_prevents_inner_and_terminal_exhaustion_has_no_fallback()
    {
        using var cancel = new CancellationTokenSource();
        using var harness = Fixture([Cp6HttpFaultOutcome.Delay(TimeSpan.FromSeconds(1))], observer: cancel.Cancel);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.SendAsync(cancel.Token));
        Assert.Equal(0, harness.Snapshot().TerminalCalls);
        using var terminal = Fixture([Cp6HttpFaultOutcome.Success, Cp6HttpFaultOutcome.Success]);
        await terminal.SendAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => terminal.SendAsync());
        Assert.Contains("TerminalExhausted", terminal.Snapshot().Errors);
    }

    [Fact]
    public async Task Cleanup_reentrant_fault_snapshot_cannot_publish_premature_complete()
    {
        DeterministicFaultCase? harness = null;
        FaultSnapshot? duringCleanup = null;
        harness = Fixture([Cp6HttpFaultOutcome.Success], cleanup: () =>
        {
            duringCleanup = harness!.Snapshot();
            throw new IOException("Synthetic cleanup failure after reentrant snapshot.");
        });
        using (harness)
        {
            Assert.Equal(HttpStatusCode.OK, await harness.SendAsync());
            Assert.Equal(CaptureState.Incomplete, harness.Complete(1, 1).State);
            Assert.NotNull(duringCleanup);
            Assert.NotEqual(CaptureState.Complete, duringCleanup.State);
            Assert.Contains("CleanupFailure", harness.Snapshot().Errors);
        }
    }

    [Fact]
    public async Task Observer_cannot_complete_active_fault_execution_or_allow_inner_after_close()
    {
        DeterministicFaultCase? harness = null;
        FaultSnapshot? premature = null;
        harness = Fixture([Cp6HttpFaultOutcome.Success], observer: () => premature = harness!.Complete(1, 0));
        using (harness)
        {
            var failure = await Record.ExceptionAsync(async () => await harness.SendAsync());
            Assert.NotNull(premature);
            Assert.NotEqual(CaptureState.Complete, premature.State);
            Assert.IsType<InvalidOperationException>(failure);
            Assert.Equal(0, harness.Snapshot().TerminalCalls);
            Assert.Equal(CaptureState.Incomplete, harness.Snapshot().State);
        }
    }
}
