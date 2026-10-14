using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace CP6.Platform.AspNetCore.ObservabilityV1;

internal sealed class PropagationLeaseV1 : IDisposable
{
    private static readonly object Gate = new();
    private static int leases;
    private static DistributedContextPropagator? previous;
    private static TextMapPropagator? previousOtel;
    private static readonly TextMapPropagator OwnedOtel = new TraceContextPropagator();
    private bool disposed;
    internal PropagationLeaseV1()
    {
        lock (Gate)
        {
            if (leases == 0)
            {
                previous = DistributedContextPropagator.Current; previousOtel = Propagators.DefaultTextMapPropagator;
                DistributedContextPropagator.Current = Cp6TraceContextDistributedPropagator.Instance;
                Sdk.SetDefaultTextMapPropagator(OwnedOtel);
            }
            else if (!Intact) throw new InvalidOperationException("Global propagation changed during V1 registration.");
            leases++;
        }
    }
    internal bool Intact => ReferenceEquals(DistributedContextPropagator.Current, Cp6TraceContextDistributedPropagator.Instance) && ReferenceEquals(Propagators.DefaultTextMapPropagator, OwnedOtel);
    public void Dispose()
    {
        lock (Gate)
        {
            if (disposed) return; disposed = true;
            if (--leases != 0) return;
            if (ReferenceEquals(DistributedContextPropagator.Current, Cp6TraceContextDistributedPropagator.Instance)) DistributedContextPropagator.Current = previous!;
            if (ReferenceEquals(Propagators.DefaultTextMapPropagator, OwnedOtel)) Sdk.SetDefaultTextMapPropagator(previousOtel!);
            previous = null; previousOtel = null;
        }
    }
}
