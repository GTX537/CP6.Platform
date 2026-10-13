using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;

namespace CP6.Platform.UnitTests.Context;

public sealed class TrustedContextTests
{
    [Fact]
    public async Task ValidBinding_ExposesExactContextAndLeastScopes_ThenRevokes()
    {
        var f = new ContextFixture();
        await f.Run(_ =>
        {
            var trusted = Assert.IsType<TrustedRequestContextV1>(f.Accessor.CurrentTrusted);
            Assert.Same(trusted.Context, f.Accessor.Current);
            Assert.Equal(f.Input!.Source.EffectiveActor.TenantId, trusted.Context.TenantId);
            Assert.Equal("service-reader", trusted.Context.Subject);
            Assert.Null(trusted.Context.UserId);
            Assert.Equal("correlation-1", trusted.Context.CorrelationId);
            Assert.False(trusted.Context.IsPublic);
            Assert.Equal("ctx-v1", trusted.Evidence.ContextContractVersion);
            Assert.Equal("ctx-binding-v1", trusted.Evidence.CapabilityVersion);
            Assert.Equal(["read"], trusted.Evidence.GrantedScopes);
            Assert.Equal("version-1", trusted.Evidence.RegistrationVersion);
            Assert.Equal("audit-only-human", trusted.Evidence.OriginalRequesterReference);
            Assert.Equal(ContextFixture.Now, trusted.Evidence.VerifiedAtUtc);
            return Task.CompletedTask;
        });
        Assert.Null(f.Accessor.Current);
        Assert.Null(f.Accessor.CurrentTrusted);
        Assert.Equal(1, f.Verifications);
        Assert.Equal(1, f.Resolutions);
    }

    [Theory]
    [InlineData("empty-tenant")]
    [InlineData("cross-tenant")]
    [InlineData("fake-issuer")]
    [InlineData("cross-actor")]
    [InlineData("cross-effective-actor")]
    [InlineData("scope")]
    [InlineData("revoked")]
    [InlineData("unregistered-region")]
    public async Task UntrustedOrMismatchedBinding_RejectsBeforeHandler(string invalid)
    {
        var f = new ContextFixture();
        var op = f.Input!.Operation;
        var source = f.Input.Source;
        f.Input = invalid switch
        {
            "empty-tenant" => f.Input with { Operation = op with { ExpectedTenantId = Guid.Empty } },
            "cross-tenant" => f.Input with { Operation = op with { ExpectedTenantId = Guid.NewGuid() } },
            "fake-issuer" => f.Input with { Source = source with { Actor = source.Actor with { Issuer = "https://evil.example" } } },
            "cross-actor" => f.Input with { Source = source with { Actor = source.Actor with { Subject = "admin" } } },
            "cross-effective-actor" => f.Input with { Source = source with { EffectiveActor = source.Actor with { TenantId = Guid.NewGuid() } } },
            "scope" => f.Input with { Operation = op with { RequiredScopes = ["admin"] } },
            "unregistered-region" => f.Input with { Source = source with { Region = "unknown" } },
            _ => f.Input
        };
        if (invalid == "revoked") f.Registration = f.Registration! with { IsActive = false };

        var error = await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ =>
            throw new InvalidOperationException("Business handler must not run.")));
        Assert.Equal(ContextResolutionFailureV1.BindingRejected, error.Failure);
        Assert.Null(f.Accessor.Current);
        Assert.Null(f.Accessor.CurrentTrusted);
    }

    [Fact]
    public async Task MissingAuthenticatedSource_RejectsWithoutAuthorityOrBusiness()
    {
        var f = new ContextFixture { Input = null };
        var error = await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ =>
            throw new InvalidOperationException("must not run")));
        Assert.Equal(ContextResolutionFailureV1.AuthenticationRequired, error.Failure);
        Assert.Equal(0, f.Resolutions);
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("purpose")]
    [InlineData("host")]
    [InlineData("profile")]
    [InlineData("organization")]
    [InlineData("entry")]
    [InlineData("missing-registration")]
    [InlineData("registration-expired")]
    [InlineData("registration-not-yet-valid")]
    [InlineData("source-expired")]
    [InlineData("source-future")]
    [InlineData("source-scopes")]
    [InlineData("registration-scopes")]
    [InlineData("registration-reference")]
    [InlineData("registration-version")]
    [InlineData("authentication-reference")]
    [InlineData("missing-operation")]
    [InlineData("missing-actor")]
    [InlineData("missing-source-scopes")]
    [InlineData("empty-scope")]
    public async Task InvalidCurrentRegistrationAndSource_NeverReachBusiness(string invalid)
    {
        var f = new ContextFixture();
        var r = f.Registration!;
        var s = f.Input!.Source;
        f.Registration = invalid switch
        {
            "audience" => r with { Audience = "cp6-browser" },
            "purpose" => r with { Purpose = "admin" },
            "host" => r with { HostId = "other-host" },
            "profile" => r with { ProfileId = "other-profile" },
            "organization" => r with { OrganizationId = Guid.NewGuid() },
            "entry" => r with { EntryKind = ContextEntryKindV1.Message },
            "missing-registration" => null,
            "registration-expired" => r with { ValidUntilUtc = ContextFixture.Now },
            "registration-not-yet-valid" => r with { ValidFromUtc = ContextFixture.Now.AddTicks(1) },
            "registration-scopes" => r with { AllowedScopes = [] },
            "registration-reference" => r with { RegistrationRef = new string('x', 257) },
            "registration-version" => r with { RegistrationVersion = " " },
            _ => r
        };
        f.Input = f.Input with { Source = invalid switch
        {
            "source-expired" => s with { ExpiresAtUtc = ContextFixture.Now },
            "source-future" => s with { AuthenticatedAtUtc = ContextFixture.Now.AddTicks(1) },
            "source-scopes" => s with { Scopes = [] },
            "authentication-reference" => s with { AuthenticationReference = " " },
            "missing-actor" => s with { Actor = null! },
            "missing-source-scopes" => s with { Scopes = null! },
            "empty-scope" => s with { Scopes = ["read", " "] },
            _ => s
        } };
        if (invalid == "missing-operation") f.Input = f.Input with { Operation = null! };
        var error = await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Executor.ExecuteAsync(
            ContextEntryKindV1.Http, "verified-native-input", f, _ => throw new InvalidOperationException("must not run")));
        Assert.Equal(ContextResolutionFailureV1.BindingRejected, error.Failure);
        Assert.Null(f.Accessor.Current);
    }

    [Theory]
    [InlineData("http://issuer.example")]
    [InlineData("https://name:secret@issuer.example")]
    [InlineData("https://issuer.example?secret=value")]
    [InlineData("https://issuer.example/#fragment")]
    [InlineData(" https://issuer.example")]
    [InlineData("https://issuer.example/a b")]
    [InlineData("https://issuer.example/\npath")]
    public async Task EvenMatchingRegistration_CannotLegalizeInvalidIssuer(string issuer)
    {
        var f = new ContextFixture();
        var actor = f.Input!.Source.Actor with { Issuer = issuer };
        f.Input = f.Input with { Source = f.Input.Source with { Actor = actor, EffectiveActor = actor } };
        f.Registration = f.Registration! with { Issuer = issuer, Actor = actor, EffectiveActor = actor };
        var error = await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ =>
            throw new InvalidOperationException("must not run")));
        Assert.Equal(ContextResolutionFailureV1.BindingRejected, error.Failure);
    }

    [Fact]
    public async Task IssuerComparison_IsExactAndDoesNotNormalizeTrailingSlashOrCase()
    {
        var f = new ContextFixture();
        f.Registration = f.Registration! with { Issuer = f.Registration!.Issuer.ToUpperInvariant() + "/" };
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ => Task.CompletedTask));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Delegation_RequiresExplicitCurrentAuthorityDecision(bool approved)
    {
        var f = new ContextFixture();
        var effective = f.Input!.Source.Actor with { Subject = "delegated-service" };
        f.Input = f.Input with { Source = f.Input.Source with { EffectiveActor = effective } };
        f.Registration = f.Registration! with { EffectiveActor = effective, PermissionDecisionRef = approved ? "decision-1" : null };
        Task Run() => f.Run(_ =>
        {
            Assert.Equal(effective.Subject, f.Accessor.Current!.Subject);
            Assert.Equal("service-reader", f.Accessor.CurrentTrusted!.Evidence.Actor.Subject);
            return Task.CompletedTask;
        });
        if (approved) await Run();
        else await Assert.ThrowsAsync<ContextResolutionExceptionV1>(Run);
    }

    [Theory]
    [InlineData(ContextActorKindV1.Service)]
    [InlineData(ContextActorKindV1.Public)]
    public async Task NonHumanIdentity_CannotCarryAnImplicitUser(ContextActorKindV1 kind)
    {
        var f = new ContextFixture();
        var actor = f.Input!.Source.Actor with { Kind = kind, UserId = Guid.NewGuid() };
        f.Input = f.Input with { Source = f.Input.Source with { Actor = actor, EffectiveActor = actor } };
        f.Registration = f.Registration! with { Actor = actor, EffectiveActor = actor, PublicRouteAllowed = true };
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ => Task.CompletedTask));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicContext_RequiresExplicitOperationRegistration_AndGrantsNoImplicitScopes(bool allowed)
    {
        var f = new ContextFixture();
        var actor = f.Input!.Source.Actor with { Kind = ContextActorKindV1.Public };
        f.Input = f.Input with { Source = f.Input.Source with { Actor = actor, EffectiveActor = actor },
            Operation = f.Input.Operation with { RequiredScopes = [] } };
        f.Registration = f.Registration! with { Actor = actor, EffectiveActor = actor, PublicRouteAllowed = allowed };
        Task Run() => f.Run(_ =>
        {
            Assert.True(f.Accessor.Current!.IsPublic);
            Assert.Null(f.Accessor.Current.UserId);
            Assert.Empty(f.Accessor.CurrentTrusted!.Evidence.GrantedScopes);
            return Task.CompletedTask;
        });
        if (allowed) await Run();
        else await Assert.ThrowsAsync<ContextResolutionExceptionV1>(Run);
    }

    [Fact]
    public async Task Collections_AreFrozenBeforeAuthorityAwait_AndEvidenceCannotBeMutated()
    {
        var f = new ContextFixture();
        var required = new[] { "read" };
        var sourceScopes = new[] { "read", "write" };
        var allowed = new[] { "read", "write" };
        f.Input = f.Input! with { Operation = f.Input!.Operation with { RequiredScopes = required },
            Source = f.Input.Source with { Scopes = sourceScopes } };
        f.Registration = f.Registration! with { AllowedScopes = allowed };
        f.Resolve = async _ =>
        {
            required[0] = "admin";
            sourceScopes[0] = "admin";
            await Task.Yield();
            Assert.Equal(["read"], f.ObservedOperation!.RequiredScopes);
            Assert.Equal(["read", "write"], f.ObservedSource!.Scopes);
            return f.Registration;
        };
        await f.Run(_ =>
        {
            allowed[0] = "admin";
            var granted = f.Accessor.CurrentTrusted!.Evidence.GrantedScopes;
            Assert.Equal(["read"], granted);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)granted)[0] = "admin");
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task HumanNullableIdentityAndOriginalStrings_ArePreservedWithoutNewLegacyLimits()
    {
        var f = new ContextFixture();
        var subject = " " + new string('s', 400) + " ";
        var audience = " " + new string('a', 400) + " ";
        var correlation = " " + new string('c', 400) + " ";
        var actor = f.Input!.Source.Actor with { Kind = ContextActorKindV1.Human, Subject = subject, UserId = null };
        f.Input = f.Input with { Operation = f.Input.Operation with { Audience = audience, CorrelationId = correlation },
            Source = f.Input.Source with { Actor = actor, EffectiveActor = actor, Audience = audience, TokenId = " " } };
        f.Registration = f.Registration! with { Actor = actor, EffectiveActor = actor, Audience = audience };
        await f.Run(_ =>
        {
            Assert.Equal(subject, f.Accessor.Current!.Subject);
            Assert.Equal(audience, f.Accessor.Current.Audience);
            Assert.Equal(correlation, f.Accessor.Current.CorrelationId);
            Assert.Null(f.Accessor.Current.TokenId);
            Assert.Null(f.Accessor.Current.UserId);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task AuthorityUnavailable_IsSafeTypedFailure_WithNoFallback()
    {
        var f = new ContextFixture { Resolve = _ => throw new ContextResolutionExceptionV1(ContextResolutionFailureV1.AuthorityUnavailable) };
        var error = await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ => Task.CompletedTask));
        Assert.Equal(ContextResolutionFailureV1.AuthorityUnavailable, error.Failure);
        Assert.Equal("Context authority is unavailable.", error.Message);
        Assert.Null(error.InnerException);
        Assert.Null(f.Accessor.Current);
    }

    [Fact]
    public void Evidence_CannotConstructOrReactivateTrustedContext()
    {
        Assert.Empty(typeof(TrustedRequestContextV1).GetConstructors());
        Assert.All(typeof(TrustedRequestContextV1).GetProperties(), p => Assert.False(p.CanWrite));
        Assert.DoesNotContain(typeof(TrustedContextExecutorV1).GetMethods().SelectMany(m => m.GetParameters()),
            p => p.ParameterType == typeof(ContextResolutionEvidenceV1) || p.ParameterType == typeof(TrustedRequestContextV1));
    }

    [Fact]
    public async Task ExpiryIsCheckedAfterAuthorityAwait_NotBeforeIt()
    {
        var clock = new MovingTimeProvider();
        var f = new ContextFixture(timeProvider: clock);
        f.Resolve = async _ =>
        {
            await Task.Yield();
            clock.Now = f.Input!.Source.ExpiresAtUtc;
            return f.Registration;
        };
        await Assert.ThrowsAsync<ContextResolutionExceptionV1>(() => f.Run(_ =>
            throw new InvalidOperationException("must not run after source expiry")));
        Assert.Null(f.Accessor.Current);
    }

    private sealed class MovingTimeProvider : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = ContextFixture.Now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
