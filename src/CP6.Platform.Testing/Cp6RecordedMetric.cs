using System.Collections.Immutable;

namespace CP6.Platform.Testing;

public sealed record Cp6RecordedMetric(
    long Sequence,
    string MeterName,
    string InstrumentName,
    double Value,
    IImmutableDictionary<string, object?> Tags);
