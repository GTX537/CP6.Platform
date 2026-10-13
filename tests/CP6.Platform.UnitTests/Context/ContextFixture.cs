using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;

namespace CP6.Platform.UnitTests.Context;

// Recording fixtures exercise the library boundary; they are not production host authentication adapters.
internal sealed class ContextFixture : IContextSourceVerifierV1<string>, IContextBindingAuthorityV1
{
    internal static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-13T00:00:00Z");
    public ContextResolutionInputV1? Input { get; set; }
    public ContextBindingRegistrationV1? Registration { get; set; }
    public int Verifications { get; private set; }
    public int Resolutions { get; private set; }
    public ContextScopeAccessorV1 Accessor { get; } = new();
    public TrustedContextExecutorV1 Executor { get; }
    public Func<CancellationToken, ValueTask<ContextResolutionInputV1?>>? Verify { get; set; }
    public Func<CancellationToken, ValueTask<ContextBindingRegistrationV1?>>? Resolve { get; set; }
    public ContextOperationV1? ObservedOperation { get; private set; }
    public AuthenticatedContextSourceV1? ObservedSource { get; private set; }

    public ContextFixture(ContextEntryKindV1 kind = ContextEntryKindV1.Http, Guid? tenant = null,
        ContextScopeAccessorV1? accessor = null, TimeProvider? timeProvider = null)
    {
        var id = tenant ?? Guid.NewGuid();
        var actor = new ContextActorV1(id, "https://issuer.example/tenant/" + id,
            "service-reader", ContextActorKindV1.Service, null);
        Input = new(
            new(kind, "host", "profile", "cp6-service", "read", id, "operation-1", "correlation-1", ["read"]),
            new(actor, actor, "cp6-service", "region-1", Now.AddMinutes(-1), Now.AddMinutes(5),
                "authenticated-source-1", "jti-1", ["read", "write"], "identity-v1", "audit-only-human"));
        Registration = new(id, id, "host", "profile", actor.Issuer, "region-1", "cp6-service", "read", kind,
            actor, actor, "registration-1", "version-1", Now.AddHours(-1), Now.AddHours(1), true, ["read", "write"]);
        Accessor = accessor ?? new();
        Executor = new(this, Accessor, timeProvider ?? new FixedTimeProvider());
    }

    public ValueTask<ContextResolutionInputV1?> VerifyAsync(string input, CancellationToken cancellationToken = default)
    {
        Assert.Equal("verified-native-input", input);
        Verifications++;
        return Verify?.Invoke(cancellationToken) ?? ValueTask.FromResult(Input);
    }

    public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(
        ContextOperationV1 operation, AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default)
    {
        Resolutions++;
        ObservedOperation = operation;
        ObservedSource = source;
        return Resolve?.Invoke(cancellationToken) ?? ValueTask.FromResult(Registration);
    }

    public Task Run(Func<CancellationToken, Task> handler, CancellationToken cancellationToken = default) =>
        Executor.ExecuteAsync(Input?.Operation.EntryKind ?? ContextEntryKindV1.Http,
            "verified-native-input", this, handler, cancellationToken);

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
