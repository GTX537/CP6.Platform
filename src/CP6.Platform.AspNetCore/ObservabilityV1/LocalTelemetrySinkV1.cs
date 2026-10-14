using System.Collections.ObjectModel;
using System.Threading.Channels;
using CP6.Platform.Contracts.ObservabilityV1;

namespace CP6.Platform.AspNetCore.ObservabilityV1;

/// <summary>Safe final export shape: no arbitrary strings, identifiers, events, links, exception or logging payloads.</summary>
public sealed class SafeTelemetryRecordV1
{
    internal SafeTelemetryRecordV1(TelemetrySignalV1 signal, string name, double value, long count, IReadOnlyDictionary<string, string> attributes, TelemetryPolicyV1 policy)
    {
        Signal = signal; Name = name; Value = value; Count = count;
        Attributes = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(attributes));
        Resources = policy.Resources;
    }
    public string ContractVersion => "1.0.0";
    public TelemetrySignalV1 Signal { get; }
    public string Name { get; }
    public double Value { get; }
    public long Count { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }
    public IReadOnlyDictionary<string, string> Resources { get; }
}

/// <summary>Bounded process-memory sink only. No destination, credentials, disk storage or network exporter.</summary>
public sealed class LocalTelemetrySinkV1
{
    private readonly Queue<SafeTelemetryRecordV1> records = new();
    private readonly int capacity;
    private long overwritten;
    public LocalTelemetrySinkV1(int capacity = 1024)
    {
        if (capacity is < 1 or > 4096) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }
    internal Action<IReadOnlyList<SafeTelemetryRecordV1>>? BeforeWrite { get; set; }
    public long OverwrittenCount => Interlocked.Read(ref overwritten);
    internal void Write(IReadOnlyList<SafeTelemetryRecordV1> batch)
    {
        BeforeWrite?.Invoke(batch);
        lock (records)
        {
            foreach (var record in batch)
            {
                if (records.Count == capacity) { records.Dequeue(); Interlocked.Increment(ref overwritten); }
                records.Enqueue(record);
            }
        }
    }
    public IReadOnlyList<SafeTelemetryRecordV1> Snapshot() { lock (records) return Array.AsReadOnly(records.ToArray()); }
}

internal sealed class SafeExportQueueV1 : IAsyncDisposable
{
    private static readonly SemaphoreSlim ProcessSinkSlots = new(4, 4);
    private readonly Channel<SafeTelemetryRecordV1> channel;
    private readonly LocalTelemetrySinkV1 sink;
    private readonly TelemetryLimitsV1 limits;
    private readonly CancellationTokenSource stop = new();
    private readonly Task worker;
    private readonly object rateGate = new();
    private long second;
    private int rate;
    private long dropped, accepted, completed;
    private int disposed;
    internal SafeExportQueueV1(LocalTelemetrySinkV1 sink, TelemetryLimitsV1 limits)
    {
        this.sink = sink; this.limits = limits;
        channel = Channel.CreateBounded<SafeTelemetryRecordV1>(new BoundedChannelOptions(limits.QueueCapacity) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        worker = Task.Run(DrainAsync);
    }
    internal long Dropped => Interlocked.Read(ref dropped) + sink.OverwrittenCount;
    internal void Drop() => Interlocked.Increment(ref dropped);
    internal bool TryEnqueue(SafeTelemetryRecordV1 record)
    {
        lock (rateGate)
        {
            var now = Environment.TickCount64 / 1000;
            if (second != now) { second = now; rate = 0; }
            if (disposed != 0 || ++rate > limits.RecordsPerSecond || Interlocked.Read(ref accepted) >= limits.MaximumRecords) { Drop(); return false; }
            // Writers never wait or call sink code.
            if (!channel.Writer.TryWrite(record)) { Drop(); return false; }
            Interlocked.Increment(ref accepted); return true;
        }
    }
    private async Task DrainAsync()
    {
        try
        {
            while (await channel.Reader.WaitToReadAsync(stop.Token))
            {
                var batch = new List<SafeTelemetryRecordV1>(limits.BatchSize);
                while (batch.Count < limits.BatchSize && channel.Reader.TryRead(out var record)) batch.Add(record);
                if (!ProcessSinkSlots.Wait(0)) { Interlocked.Add(ref dropped, batch.Count); Interlocked.Add(ref completed, batch.Count); continue; }
                var export = Task.Run(() => { try { sink.Write(batch); } finally { ProcessSinkSlots.Release(); } });
                try { await export.WaitAsync(limits.SinkTimeout, stop.Token); }
                catch (TimeoutException)
                {
                    Interlocked.Add(ref dropped, batch.Count);
                    // Never launch another sink invocation until this one has really returned.
                    try { await export.WaitAsync(stop.Token); } catch { }
                }
                catch { Interlocked.Add(ref dropped, batch.Count); }
                Interlocked.Add(ref completed, batch.Count);
            }
        }
        catch (OperationCanceledException) { }
        finally { while (channel.Reader.TryRead(out _)) { Drop(); Interlocked.Increment(ref completed); } }
    }
    internal async Task<bool> FlushAsync(TimeSpan timeout)
    {
        var target = Interlocked.Read(ref accepted);
        using var budget = new CancellationTokenSource(timeout);
        try { while (Interlocked.Read(ref completed) < target) await Task.Delay(5, budget.Token); return Dropped == 0; }
        catch (OperationCanceledException) { return false; }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        channel.Writer.TryComplete(); stop.Cancel();
        try { await worker.WaitAsync(TimeSpan.FromMilliseconds(200)); } catch { }
        // A stuck synchronous sink may outlive the host, but retains a process-wide slot until it exits.
    }
}
