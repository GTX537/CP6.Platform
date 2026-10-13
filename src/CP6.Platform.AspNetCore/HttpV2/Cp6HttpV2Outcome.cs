namespace CP6.Platform.AspNetCore.HttpV2;

public enum Cp6HttpOperationState { NotSent, Unknown, ResponseReceived, Applied, Rejected, Pending }
public enum Cp6HttpV2Failure
{
    None, AttemptTimeout, TotalTimeout, UnknownTimeoutSource, CircuitOpen, Transport,
    BindingRejected, OperationNotAllowed, ResponseLimitExceeded, RecoveryUnavailable, ReplayDenied
}
public sealed record Cp6HttpV2Outcome(Cp6HttpOperationState State, Cp6HttpV2Failure Failure,
    int Attempts, string PolicyVersion, Cp6HttpOperationIdentity? OriginalIdentity = null,
    Cp6RetryStop RetryStop = Cp6RetryStop.None, Cp6RetryAfterKind RetryAfterKind = Cp6RetryAfterKind.Absent)
{
    public string ContractVersion => "cp6-http-outcome-v2";
    public static readonly HttpRequestOptionsKey<Cp6HttpV2Outcome> RequestOption = new("CP6.HttpV2.Outcome");
}
public sealed class Cp6HttpV2Exception : Exception
{
    public Cp6HttpV2Exception(Cp6HttpV2Outcome outcome, Exception? innerException = null)
        : base("The bounded HTTP operation did not complete. Inspect the typed safe outcome.", innerException) => Outcome = outcome;
    public Cp6HttpV2Outcome Outcome { get; }
}
