using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.Platform.Messaging.Context;

namespace CP6.Platform.UnitTests.Context;

public sealed class ContextEntryTests
{
    [Theory]
    [InlineData(ContextEntryKindV1.Message)]
    [InlineData(ContextEntryKindV1.Background)]
    public async Task EachRetry_ReverifiesCurrentServiceAndRegistration_NotHistoricalRequester(ContextEntryKindV1 kind)
    {
        var f = new ContextFixture(kind);
        Task Run(Func<CancellationToken, Task> handler) => kind == ContextEntryKindV1.Message
            ? new MessageContextExecutorV1<string>(f.Executor, f).ExecuteAsync("verified-native-input", handler)
            : new BackgroundContextExecutorV1<string>(f.Executor, f).ExecuteAsync("verified-native-input", handler);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Run(_ =>
        {
            Assert.Equal("service-reader", f.Accessor.Current!.Subject);
            Assert.Equal("audit-only-human", f.Accessor.CurrentTrusted!.Evidence.OriginalRequesterReference);
            throw new InvalidOperationException("retryable fixture business failure");
        }));
        Assert.Null(f.Accessor.Current);
        var currentService = f.Input!.Source.Actor with { Subject = "replacement-service" };
        f.Input = f.Input with { Source = f.Input.Source with { Actor = currentService, EffectiveActor = currentService,
            SourceIdentityVersion = "identity-v2" } };
        f.Registration = f.Registration! with { Actor = currentService, EffectiveActor = currentService, RegistrationVersion = "version-2" };
        await Run(_ =>
        {
            Assert.Equal("replacement-service", f.Accessor.Current!.Subject);
            Assert.Null(f.Accessor.Current.UserId);
            Assert.Equal("version-2", f.Accessor.CurrentTrusted!.Evidence.RegistrationVersion);
            return Task.CompletedTask;
        });
        f.Registration = null; // Revoked before the next attempt, despite otherwise valid source/token.
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => Run(_ => throw new InvalidOperationException("must not run")));
        Assert.Equal(3, f.Verifications);
        Assert.Equal(3, f.Resolutions);
        Assert.Null(f.Accessor.CurrentTrusted);
    }

    [Theory]
    [InlineData(ContextEntryKindV1.Message, ContextActorKindV1.Human)]
    [InlineData(ContextEntryKindV1.Message, ContextActorKindV1.Public)]
    [InlineData(ContextEntryKindV1.Background, ContextActorKindV1.Human)]
    [InlineData(ContextEntryKindV1.Background, ContextActorKindV1.Public)]
    public async Task NonHttpEntry_RejectsHistoricalHumanOrPublicAsExecutor(ContextEntryKindV1 kind, ContextActorKindV1 actorKind)
    {
        var f = new ContextFixture(kind);
        var actor = f.Input!.Source.Actor with { Kind = actorKind };
        f.Input = f.Input with { Source = f.Input.Source with { Actor = actor, EffectiveActor = actor } };
        f.Registration = f.Registration! with { Actor = actor, EffectiveActor = actor, PublicRouteAllowed = true };
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ => Task.CompletedTask));
        Assert.Equal(0, f.Resolutions);
    }

    [Theory]
    [InlineData(ContextEntryKindV1.Message)]
    [InlineData(ContextEntryKindV1.Background)]
    public async Task EntryWrapper_RejectsCapturedHttpSource(ContextEntryKindV1 kind)
    {
        var f = new ContextFixture();
        Task Run() => kind == ContextEntryKindV1.Message
            ? new MessageContextExecutorV1<string>(f.Executor, f).ExecuteAsync("verified-native-input", _ => Task.CompletedTask)
            : new BackgroundContextExecutorV1<string>(f.Executor, f).ExecuteAsync("verified-native-input", _ => Task.CompletedTask);
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(Run);
        Assert.Null(f.Accessor.Current);
    }

    [Fact]
    public async Task MessageTraceAlone_CannotEstablishIdentity()
    {
        var f = new ContextFixture(ContextEntryKindV1.Message) { Input = null };
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => new MessageContextExecutorV1<string>(f.Executor, f)
            .ExecuteAsync("verified-native-input", _ => throw new InvalidOperationException("must not run")));
        Assert.Equal(0, f.Resolutions);
    }
}
