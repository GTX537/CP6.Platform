namespace CP6.Platform.Testing;

public sealed class Cp6HttpFaultHandler : DelegatingHandler
{
    private readonly Cp6HttpFaultScript script;
    private readonly TimeProvider timeProvider;

    public Cp6HttpFaultHandler(Cp6HttpFaultScript script, TimeProvider? timeProvider = null)
    {
        this.script = script ?? throw new ArgumentNullException(nameof(script));
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var outcome = script.Next();
        switch (outcome.Kind)
        {
            case Cp6HttpFaultOutcomeKind.Status:
                return new HttpResponseMessage(outcome.StatusCode) { RequestMessage = request };
            case Cp6HttpFaultOutcomeKind.Exception:
                throw outcome.Exception ?? new InvalidOperationException("The scripted exception outcome is invalid.");
            case Cp6HttpFaultOutcomeKind.Delay:
                await Task.Delay(outcome.DelayDuration, timeProvider, cancellationToken);
                return await base.SendAsync(request, cancellationToken);
            case Cp6HttpFaultOutcomeKind.Success:
                return await base.SendAsync(request, cancellationToken);
            default:
                throw new InvalidOperationException("The scripted HTTP fault outcome is not supported.");
        }
    }
}
