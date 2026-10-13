using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts;

namespace CP6.Platform.UnitTests.Context;

public sealed class ContextScopeTests
{
    [Fact]
    public async Task SerialAndNested_ABA_RestoreOuterAndClearTopLevel()
    {
        var a = new ContextFixture();
        var b = new ContextFixture(accessor: a.Accessor);
        foreach (var fixture in new[] { a, b, a })
        {
            await fixture.Run(async _ =>
            {
                var outer = a.Accessor.CurrentTrusted;
                Assert.Equal(fixture.Input!.Operation.ExpectedTenantId, outer!.Context.TenantId);
                await b.Run(_ =>
                {
                    Assert.Equal(b.Input!.Operation.ExpectedTenantId, a.Accessor.Current!.TenantId);
                    return Task.CompletedTask;
                });
                Assert.Same(outer, a.Accessor.CurrentTrusted);
            });
            Assert.Null(a.Accessor.Current);
        }
    }

    [Fact]
    public async Task OverlappingAsyncOperations_OnSameAccessor_KeepSeparateTenants()
    {
        var a = new ContextFixture();
        var b = new ContextFixture(accessor: a.Accessor);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        async Task Observe(ContextFixture fixture)
        {
            await fixture.Run(async _ =>
            {
                if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
                await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.Equal(fixture.Input!.Operation.ExpectedTenantId, a.Accessor.Current!.TenantId);
                await Task.Yield();
                Assert.Equal(fixture.Input.Operation.ExpectedTenantId, a.Accessor.CurrentTrusted!.Evidence.TenantId);
            });
            Assert.Null(a.Accessor.Current);
        }
        await Task.WhenAll(Observe(a), Observe(b));
        Assert.Null(a.Accessor.CurrentTrusted);
    }

    [Fact]
    public async Task CapturedChildExecutionContext_IsRevokedAfterParentScopeEnds()
    {
        var f = new ContextFixture();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? child = null;
        await f.Run(_ =>
        {
            child = Task.Run(async () =>
            {
                await release.Task;
                Assert.Null(f.Accessor.Current);
                Assert.Null(f.Accessor.CurrentTrusted);
            });
            return Task.CompletedTask;
        });
        release.SetResult();
        await child!;
    }

    [Theory]
    [InlineData("verifier")]
    [InlineData("authority")]
    [InlineData("handler")]
    public async Task ExceptionsDuringEachPhase_ClearScopeAndRestoreOuter(string phase)
    {
        var outer = new ContextFixture();
        var inner = new ContextFixture(accessor: outer.Accessor);
        var failure = new InvalidOperationException("fixture failure");
        if (phase == "verifier") inner.Verify = _ => throw failure;
        if (phase == "authority") inner.Resolve = _ => throw failure;
        await outer.Run(async _ =>
        {
            var original = outer.Accessor.CurrentTrusted;
            var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => inner.Run(_ => throw failure));
            Assert.Same(failure, actual);
            Assert.Same(original, outer.Accessor.CurrentTrusted);
        });
        Assert.Null(outer.Accessor.Current);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("verifier")]
    [InlineData("authority")]
    [InlineData("handler")]
    public async Task CancellationInEachPhase_PropagatesAndClears(string phase)
    {
        var f = new ContextFixture();
        using var cancellation = new CancellationTokenSource();
        if (phase == "before") cancellation.Cancel();
        f.Verify = ct =>
        {
            Assert.Null(f.Accessor.Current);
            Assert.Equal(cancellation.Token, ct);
            if (phase == "verifier") cancellation.Cancel();
            return ValueTask.FromResult(f.Input);
        };
        f.Resolve = ct =>
        {
            Assert.Null(f.Accessor.CurrentTrusted);
            Assert.Equal(cancellation.Token, ct);
            if (phase == "authority") cancellation.Cancel();
            return ValueTask.FromResult(f.Registration);
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Run(ct =>
        {
            Assert.Equal("handler", phase);
            cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }, cancellation.Token));
        Assert.Null(f.Accessor.Current);
        Assert.Null(f.Accessor.CurrentTrusted);
    }

    [Fact]
    public async Task ResolutionMasksOuterContext_AndLegacyDoesNotInheritTrustedEvidence()
    {
        var f = new ContextFixture();
        await f.Run(async _ =>
        {
            var outer = f.Accessor.CurrentTrusted;
            var snapshot = new RequestContextSnapshot(Guid.NewGuid(), null, "legacy", "aud", "corr", null, false);
            Assert.True(await f.Accessor.ExecuteLegacyAsync(_ =>
            {
                Assert.Null(f.Accessor.Current);
                Assert.Null(f.Accessor.CurrentTrusted);
                return ValueTask.FromResult<RequestContextSnapshot?>(snapshot);
            }, _ =>
            {
                Assert.Equal(snapshot.TenantId, f.Accessor.Current!.TenantId);
                Assert.Null(f.Accessor.CurrentTrusted);
                return Task.CompletedTask;
            }));
            Assert.Same(outer, f.Accessor.CurrentTrusted);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyNullOrInvalidSnapshot_DoesNotInvokeHandlerOrLeak(bool invalid)
    {
        var accessor = new ContextScopeAccessorV1();
        RequestContextSnapshot? snapshot = invalid ? new(Guid.Empty, null, "sub", "aud", "corr", null, false) : null;
        Assert.False(await accessor.ExecuteLegacyAsync(_ => ValueTask.FromResult(snapshot),
            _ => throw new InvalidOperationException("must not run")));
        Assert.Null(accessor.Current);
    }

    [Fact]
    public async Task PendingAuthorityCancellation_ClearsMaskedNestedScopeAndRestoresOuter()
    {
        var outer = new ContextFixture();
        var inner = new ContextFixture(accessor: outer.Accessor);
        using var cts = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        inner.Resolve = async ct =>
        {
            Assert.Null(inner.Accessor.Current);
            entered.SetResult();
            await Task.Delay(Timeout.Infinite, ct);
            return inner.Registration;
        };
        await outer.Run(async _ =>
        {
            var original = outer.Accessor.CurrentTrusted;
            var pending = inner.Run(_ => throw new InvalidOperationException("must not run"), cts.Token);
            await entered.Task;
            Assert.Same(original, outer.Accessor.CurrentTrusted);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
            Assert.Same(original, outer.Accessor.CurrentTrusted);
        });
        Assert.Null(outer.Accessor.Current);
    }
}
