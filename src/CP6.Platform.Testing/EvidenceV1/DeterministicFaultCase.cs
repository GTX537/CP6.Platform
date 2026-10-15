using System.Net;

namespace CP6.Platform.Testing.EvidenceV1;

/// <summary>Case-owned virtual UTC/monotonic clock. Advancing never waits on wall time.</summary>
public sealed class CaseClock : TimeProvider
{
    private readonly object gate = new();
    private DateTimeOffset now;
    private long ticks;
    public CaseClock(DateTimeOffset epoch) { if (epoch.Offset != TimeSpan.Zero) throw new ArgumentException("UTC required."); now = epoch; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() { lock (gate) return now; }
    public override long GetTimestamp() { lock (gate) return ticks; }
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        lock (gate) { var next = checked(ticks + duration.Ticks); var nextUtc = now.Add(duration); ticks = next; now = nextUtc; }
    }
}
public sealed class CaseRandom
{
    private uint state;
    public CaseRandom(uint seed) { if (seed == 0) throw new ArgumentOutOfRangeException(nameof(seed)); state = seed; }
    public uint Next() { lock (this) { state ^= state << 13; state ^= state >> 17; state ^= state << 5; return state; } }
}
public enum RetryPlacement { RetryOutsideFault, RetryInsideFault }
public sealed record FaultSnapshot(string CaseId, string TerminalName, RetryPlacement Placement, int ScriptCalls,
    int TerminalCalls, CaptureState State, IReadOnlyList<string> Errors)
{ public bool ProductionSloClaimed => false; }

/// <summary>Sealed finite in-memory pipeline: it cannot accept a network handler or fall back to HTTP.</summary>
public sealed class DeterministicFaultCase : IDisposable
{
    private readonly object gate = new();
    private readonly Cp6HttpFaultOutcome[] script;
    private readonly HttpStatusCode[] terminal;
    private readonly int maxScriptCalls;
    private readonly int maxTerminalCalls;
    private readonly int retryCount;
    private readonly Action? observer;
    private readonly Action? cleanup;
    private readonly HashSet<string> errors = new(StringComparer.Ordinal);
    private bool closed;
    private bool disposed;
    private bool completionValidated;
    private bool cleanupFinished;
    private bool executing;
    private int scriptCalls;
    private int terminalCalls;
    public DeterministicFaultCase(string caseId, string terminalName, IEnumerable<Cp6HttpFaultOutcome> script,
        IEnumerable<HttpStatusCode> terminalResponses, RetryPlacement placement, int retryCount, int maximumScriptCalls,
        int maximumTerminalCalls, DateTimeOffset epoch, uint seed, Action? observer = null, Action? cleanup = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(caseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalName);
        if (!terminalName.StartsWith("in-memory:", StringComparison.Ordinal)) throw new ArgumentException("Exclusively named in-memory terminal required.");
        if (!Enum.IsDefined(placement) || retryCount is < 0 or > 100 || maximumScriptCalls is < 1 or > 10000 || maximumTerminalCalls is < 1 or > 10000) throw new ArgumentOutOfRangeException(nameof(retryCount));
        this.script = script.Take(10001).ToArray(); this.terminal = terminalResponses.Take(10001).ToArray();
        if (this.script.Length is 0 or > 10000 || this.script.Any(x => x is null) || this.terminal.Length is 0 or > 10000 || this.terminal.Any(x => (int)x is < 100 or > 599)) throw new ArgumentException("Finite valid script and terminal responses required.");
        CaseId = caseId; TerminalName = terminalName; Placement = placement; this.retryCount = retryCount;
        maxScriptCalls = maximumScriptCalls; maxTerminalCalls = maximumTerminalCalls; this.observer = observer; this.cleanup = cleanup;
        Clock = new(epoch); Random = new(seed);
    }
    public string CaseId { get; }
    public string TerminalName { get; }
    public RetryPlacement Placement { get; }
    public CaseClock Clock { get; }
    public CaseRandom Random { get; }
    public Task<HttpStatusCode> SendAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            try
            {
                if (closed || disposed || executing) throw new InvalidOperationException("Fault case is closed or already executing.");
                cancellationToken.ThrowIfCancellationRequested();
                executing = true;
                try
                {
                    return Task.FromResult(Placement == RetryPlacement.RetryOutsideFault
                        ? Retry(() => Fault(() => Terminal(cancellationToken), cancellationToken), cancellationToken)
                        : Fault(() => Retry(() => Terminal(cancellationToken), cancellationToken), cancellationToken));
                }
                finally { executing = false; }
            }
            catch (Exception ex)
            {
                errors.Add(ex is OperationCanceledException ? "Cancelled" : "ExecutionFailure");
                return Task.FromException<HttpStatusCode>(ex);
            }
        }
    }
    private HttpStatusCode Retry(Func<HttpStatusCode> next, CancellationToken token)
    {
        for (var retry = 0; ; retry++)
        {
            token.ThrowIfCancellationRequested();
            var status = next();
            if ((int)status < 500 || retry >= retryCount) return status;
        }
    }
    private HttpStatusCode Fault(Func<HttpStatusCode> inner, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (scriptCalls >= maxScriptCalls || scriptCalls >= script.Length) { errors.Add("ScriptExhausted"); throw new InvalidOperationException("Finite script exhausted."); }
        var outcome = script[scriptCalls++]; Observe();
        if (closed || disposed) throw new InvalidOperationException("Fault case closed during observer callback.");
        token.ThrowIfCancellationRequested();
        switch (outcome.Kind)
        {
            case Cp6HttpFaultOutcomeKind.Status: return outcome.StatusCode;
            case Cp6HttpFaultOutcomeKind.Exception: throw outcome.Exception!;
            case Cp6HttpFaultOutcomeKind.Delay: Clock.Advance(outcome.DelayDuration); token.ThrowIfCancellationRequested(); return inner();
            case Cp6HttpFaultOutcomeKind.Success: return inner();
            default: throw new InvalidOperationException("Unknown scripted outcome.");
        }
    }
    private HttpStatusCode Terminal(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (terminalCalls >= maxTerminalCalls || terminalCalls >= terminal.Length) { errors.Add("TerminalExhausted"); throw new InvalidOperationException("Finite in-memory terminal exhausted."); }
        return terminal[terminalCalls++];
    }
    private void Observe() { try { observer?.Invoke(); } catch (Exception) { errors.Add("ObserverFailure"); } }
    public FaultSnapshot Complete(int expectedScriptCalls, int expectedTerminalCalls)
    {
        lock (gate)
        {
            if (disposed || closed) errors.Add("CompletionAfterClose");
            closed = true;
            if (executing) errors.Add("CompletionDuringExecution");
            else
            {
                if (expectedScriptCalls < 1 || scriptCalls != expectedScriptCalls || terminalCalls != expectedTerminalCalls) errors.Add("CallCountMismatch");
                completionValidated = true;
            }
            Dispose();
            return Snapshot();
        }
    }
    public FaultSnapshot Snapshot()
    {
        lock (gate) return new(CaseId, TerminalName, Placement, scriptCalls, terminalCalls,
            errors.Count == 0 && completionValidated && cleanupFinished && !executing ? CaptureState.Complete : errors.Count == 0 && !disposed ? CaptureState.Capturing : CaptureState.Incomplete,
            Array.AsReadOnly(errors.Order(StringComparer.Ordinal).ToArray()));
    }
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (!closed) errors.Add("DisposedBeforeCompletion");
            try { cleanup?.Invoke(); } catch (Exception) { errors.Add("CleanupFailure"); }
            cleanupFinished = true;
        }
    }
}
