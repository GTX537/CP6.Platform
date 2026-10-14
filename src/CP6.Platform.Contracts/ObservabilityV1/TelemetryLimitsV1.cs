namespace CP6.Platform.Contracts.ObservabilityV1;

public enum TelemetrySignalV1 { Trace, Metric, Log }

public sealed class TelemetryLimitsV1
{
    public TelemetryLimitsV1(int queueCapacity = 256, int maximumRecords = 1024, int batchSize = 16, int recordsPerSecond = 512, int maximumSeries = 128)
    {
        if (queueCapacity is < 1 or > 4096 || maximumRecords is < 1 or > 4096 || batchSize < 1 || batchSize > Math.Min(queueCapacity, 128) || recordsPerSecond is < 1 or > 4096 || maximumSeries is < 1 or > 256)
            throw new ArgumentOutOfRangeException(nameof(queueCapacity), "Telemetry bounds are invalid.");
        QueueCapacity = queueCapacity; MaximumRecords = maximumRecords; BatchSize = batchSize; RecordsPerSecond = recordsPerSecond; MaximumSeries = maximumSeries;
    }
    public int QueueCapacity { get; }
    public int MaximumRecords { get; }
    public int BatchSize { get; }
    public int RecordsPerSecond { get; }
    public int MaximumSeries { get; }
    public int MaximumAttributes => 12;
    public int MaximumRecordBytes => 4096;
    public TimeSpan SinkTimeout => TimeSpan.FromMilliseconds(100);
}
