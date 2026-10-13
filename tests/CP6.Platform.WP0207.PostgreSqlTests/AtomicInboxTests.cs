using System.Data;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.InboxV1;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Platform.WP0207.PostgreSqlTests;

[Collection("real-pg")]
public sealed class AtomicInboxTests(PgFixture pg, ITestOutputHelper output)
{
    private async Task<PendingCommitResult> Process(Cp6InboxDelivery d, FixtureOwner owner, bool commit = true)
    {
        var identity = new TestIdentity(d.TenantId);
        PendingCommitResult? pending = null;
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            pending = await new CallerTransactionInboxV1(identity.Accessor, owner).ProcessWithPreCheckpointV1(db, tx.GetDbTransaction(), d);
            output.WriteLine($"decision={pending.Decision}; receipt={pending.ReceiptId}; original-key={pending.OperationKey}");
            if (commit) await tx.CommitAsync(); else await tx.RollbackAsync();
        });
        return pending!;
    }
    private async Task<CommittedDeliveryOutcome?> Read(Cp6InboxDelivery d, PendingCommitResult pending, FixtureOwner owner, Guid? asTenant = null, string? digest = null)
    {
        var identity = new TestIdentity(asTenant ?? d.TenantId);
        CommittedDeliveryOutcome? result = null;
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
            result = await new CallerTransactionInboxV1(identity.Accessor, owner).ReadOriginalResultAsync(db, tx.GetDbTransaction(),
                "fixture-owner", FixtureOwner.Key(d), "1", "fixture-owner-input", "fixture-canonical-v1", digest ?? FixtureOwner.InputDigest(d), pending.ReceiptId, owner);
            await tx.RollbackAsync();
        });
        return result;
    }
    private async Task AssertEmpty(Guid tenant)
    {
        await using var db = await pg.OpenAsync();
        Assert.False(await db.Set<BusinessEffect>().AnyAsync(x => x.Tenant == tenant));
        Assert.False(await db.Set<Journal>().AnyAsync(x => x.Tenant == tenant));
        Assert.False(await db.Set<Projection>().AnyAsync(x => x.Tenant == tenant));
        Assert.False(await db.Set<Published>().AnyAsync(x => x.Tenant == tenant));
        Assert.False(await db.Set<Observation>().AnyAsync(x => x.Tenant == tenant));
        Assert.False(await db.Set<InboxTransportV1>().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await db.Set<InboxCheckpointV1>().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await db.Set<InboxOriginalResultV1>().AnyAsync(x => x.TenantId == tenant));
        Assert.False(await db.Set<InboxDeliveryOutcomeV1>().AnyAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task Pending_is_invisible_until_caller_commit_and_all_nine_tables_commit_together()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); var identity = new TestIdentity(d.TenantId);
        PendingCommitResult? pending = null;
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync();
            await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            pending = await new CallerTransactionInboxV1(identity.Accessor, owner).ProcessWithPreCheckpointV1(db, tx.GetDbTransaction(), d);
            Assert.Equal(PreCheckpointIdentityDecisionV1.FreshCandidate, pending.Decision);
            Assert.NotNull(tx.GetDbTransaction().Connection);
            Assert.Null(await Read(d, pending, owner));
            await AssertEmpty(d.TenantId);
            await tx.CommitAsync();
        });
        var committed = await Read(d, pending!, owner);
        Assert.Equal("SUCCESS", committed!.TransportOutcome);
        await using var check = await pg.OpenAsync();
        Assert.Single(await check.Set<BusinessEffect>().Where(x => x.Tenant == d.TenantId).ToArrayAsync());
        var journal = await check.Set<Journal>().SingleAsync(x => x.Tenant == d.TenantId);
        Assert.Equal(journal.Result, committed.VisibleResult.ToArray());
        Assert.Equal(FixtureOwner.Hash(journal.Result), committed.OriginalResultSha256);
        Assert.Single(await check.Set<Projection>().Where(x => x.Tenant == d.TenantId).ToArrayAsync());
        Assert.True((await check.Set<Published>().SingleAsync(x => x.Tenant == d.TenantId)).Allow);
        var observation = await check.Set<Observation>().SingleAsync(x => x.Tenant == d.TenantId);
        output.WriteLine("actual native identity=" + observation.NativeIdentity);
        Assert.Contains("wp0207_runtime_", observation.NativeIdentity);
        Assert.Single(await check.Set<InboxTransportV1>().Where(x => x.TenantId == d.TenantId).ToArrayAsync());
        Assert.Single(await check.Set<InboxCheckpointV1>().Where(x => x.TenantId == d.TenantId).ToArrayAsync());
        Assert.Single(await check.Set<InboxOriginalResultV1>().Where(x => x.TenantId == d.TenantId).ToArrayAsync());
        Assert.Single(await check.Set<InboxDeliveryOutcomeV1>().Where(x => x.TenantId == d.TenantId).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Explicit_rollback_or_failure_after_owner_save_leaves_no_partial_graph(bool failure)
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner { FailAfterSave = failure };
        if (failure) await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Process(d, owner));
        else { var p = await Process(d, owner, false); Assert.Null(await Read(d, p, owner)); }
        await AssertEmpty(d.TenantId);
    }

    [Fact]
    public async Task Duplicate_old_equal_and_known_historical_conflict_all_pass_the_content_gate()
    {
        var tenant = Guid.NewGuid(); var owner = new FixtureOwner(); var d = FixtureOwner.Delivery(tenant);
        await Process(d, owner);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportDuplicate, (await Process(d, owner)).Decision);
        var equal = FixtureOwner.Delivery(tenant, d.AggregateId, version: 1, whitespace: true);
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionEqual, (await Process(equal, owner)).Decision);
        var newer = FixtureOwner.Delivery(tenant, d.AggregateId, version: 3);
        await Process(newer, owner);
        var old = FixtureOwner.Delivery(tenant, d.AggregateId, version: 1);
        Assert.Equal(PreCheckpointIdentityDecisionV1.OlderVersionIgnored, (await Process(old, owner)).Decision);
        var conflict = FixtureOwner.Delivery(tenant, d.AggregateId, version: 1, value: 99);
        var p = await Process(conflict, owner);
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionConflict, p.Decision);
        Assert.Equal("DROP", (await Read(conflict, p, owner))!.TransportOutcome);
        Assert.Equal(PreCheckpointIdentityDecisionV1.SameVersionConflict, (await Process(d, owner)).Decision);
        Assert.Equal(7, owner.Evaluations);
        await using var db = await pg.OpenAsync();
        var projection = await db.Set<Projection>().SingleAsync(x => x.Tenant == tenant);
        Assert.Equal(3, projection.Version); Assert.True(projection.Drift);
        Assert.False((await db.Set<Published>().SingleAsync(x => x.Tenant == tenant)).Allow);
        Assert.Equal(2, await db.Set<BusinessEffect>().CountAsync(x => x.Tenant == tenant));
        Assert.Equal(3, (await db.Set<InboxCheckpointV1>().SingleAsync(x => x.TenantId == tenant)).Version);
    }

    [Fact]
    public async Task Same_message_different_raw_or_binding_has_priority_and_preserves_original_result()
    {
        var tenant = Guid.NewGuid(); var owner = new FixtureOwner(); var d = FixtureOwner.Delivery(tenant);
        var first = await Process(d, owner); var original = await Read(d, first, owner);
        var changed = FixtureOwner.Delivery(tenant, d.AggregateId, d.MessageId, whitespace: true);
        owner.Available = false;
        var conflict = await Process(changed, owner);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict, conflict.Decision);
        Assert.Equal("DROP", (await Read(changed, conflict, owner))!.TransportOutcome);
        var binding = changed with { TopicName = "different-registered-topic" };
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict, (await Process(binding, owner)).Decision);
        Assert.Equal(original!.VisibleResult.ToArray(), (await Read(d, first, owner))!.VisibleResult.ToArray());
        await using var db = await pg.OpenAsync();
        Assert.Equal(FixtureOwner.Hash(d.Payload.Span), (await db.Set<InboxTransportV1>().SingleAsync(x => x.TenantId == tenant)).RawSha256);
    }

    [Fact]
    public async Task Unavailable_anchor_never_completes_duplicate_or_checkpoint()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); await Process(d, owner);
        owner.Available = false;
        var pending = await Process(d, owner);
        Assert.Equal(PreCheckpointIdentityDecisionV1.DependencyUnavailable, pending.Decision);
        Assert.Equal(Guid.Empty, pending.ReceiptId); Assert.Null(await Read(d, pending, owner));
        Assert.Equal(1, owner.Applications);
    }

    [Fact]
    public async Task Lost_commit_confirmation_queries_original_bytes_without_a_new_write_grant()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner();
        // Real COMMIT succeeds; the simulated application channel discards confirmation before responding.
        var pending = await Process(d, owner);
        owner.AllowWrite = false;
        var recovered = await Read(d, pending, owner);
        Assert.Equal("SUCCESS", recovered!.TransportOutcome);
        Assert.Equal(1, owner.Applications);
        Assert.Null(await Read(d, pending, owner, Guid.NewGuid()));
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Read(d, pending, owner, digest: new string('a', 64)));
        owner.AllowRead = false;
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Read(d, pending, owner));
        await using var db = await pg.OpenAsync();
        Assert.Equal(1, await db.Set<BusinessEffect>().CountAsync(x => x.Tenant == d.TenantId));
    }

    [Fact]
    public async Task Missing_wrong_connection_ended_transaction_and_wrong_tenant_are_rejected()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var identity = new TestIdentity(d.TenantId); var owner = new FixtureOwner();
        await identity.Run(async () =>
        {
            var processor = new CallerTransactionInboxV1(identity.Accessor, owner);
            await using var a = await pg.OpenAsync(); await using var b = await pg.OpenAsync();
            await using var at = await a.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => processor.ProcessWithPreCheckpointV1(b, at.GetDbTransaction(), d));
            await using var bt = await b.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => processor.ProcessWithPreCheckpointV1(a, bt.GetDbTransaction(), d));
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => processor.ProcessWithPreCheckpointV1(a, at.GetDbTransaction(), d with { TenantId = Guid.NewGuid() }));
            var native = at.GetDbTransaction(); await at.RollbackAsync();
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => processor.ProcessWithPreCheckpointV1(a, native, d));
        });
        Assert.Equal(0, owner.Evaluations); await AssertEmpty(d.TenantId);
    }

    [Fact]
    public async Task Wrong_lock_order_and_preheld_late_lock_are_not_accepted_as_proof()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid());
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Process(d, new FixtureOwner { BadLockOrder = true }));
        var identity = new TestIdentity(d.TenantId); var owner = new FixtureOwner();
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(420007)");
            var ex = await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => new CallerTransactionInboxV1(identity.Accessor, owner)
                .ProcessWithPreCheckpointV1(db, tx.GetDbTransaction(), d));
            Assert.Equal("CP6_INBOX_PRIOR_LOCK_ORDER_UNPROVEN", ex.Code);
        });
        await AssertEmpty(d.TenantId);
    }

    [Fact]
    public async Task Real_transaction_locks_are_held_until_caller_rollback()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var identity = new TestIdentity(d.TenantId);
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new CallerTransactionInboxV1(identity.Accessor, new FixtureOwner()).ProcessWithPreCheckpointV1(db, tx.GetDbTransaction(), d);
            await using var contender = new NpgsqlConnection(pg.RuntimeConnection); await contender.OpenAsync();
            await using var competingTx = await contender.BeginTransactionAsync();
            for (var key = 420001; key <= 420007; key++)
                Assert.False((bool)(await new NpgsqlCommand($"SELECT pg_try_advisory_xact_lock({key})", contender, competingTx).ExecuteScalarAsync())!);
            await tx.RollbackAsync();
            Assert.True((bool)(await new NpgsqlCommand("SELECT pg_try_advisory_xact_lock(420001)", contender, competingTx).ExecuteScalarAsync())!);
        });
    }

    [Fact]
    public async Task Same_key_competition_recovers_whole_original_input_through_a_new_content_gate()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid());
        var locked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a = new FixtureOwner { AfterAuthorityRead = async _ => { locked.SetResult(); await release.Task; } };
        var first = Process(d, a); await locked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var b = new FixtureOwner(); var second = Process(d, b);
        // Wait until PostgreSQL reports the real competing backend blocked on an advisory lock.
        await using (var monitor = new NpgsqlConnection(pg.RuntimeConnection))
        {
            await monitor.OpenAsync(); var until = DateTime.UtcNow.AddSeconds(10); var waiting = false;
            while (DateTime.UtcNow < until)
            {
                waiting = (bool)(await new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND NOT granted)", monitor).ExecuteScalarAsync())!;
                if (waiting) break; await Task.Delay(20);
            }
            Assert.True(waiting);
        }
        release.SetResult(); await first;
        try { await second; Assert.Fail("Expected stale serializable snapshot to abort the competing attempt"); }
        catch (Exception ex) when (FindPg(ex) is { SqlState: "40001" or "23505" } pgError)
        { output.WriteLine("real concurrency failure=" + pgError.SqlState); }
        var before = b.Evaluations;
        var recovered = await Process(d, b);
        Assert.True(b.Evaluations > before);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportDuplicate, recovered.Decision);
        await using var check = await pg.OpenAsync();
        Assert.Equal(1, await check.Set<BusinessEffect>().CountAsync(x => x.Tenant == d.TenantId));
        Assert.Equal(1, await check.Set<Journal>().CountAsync(x => x.Tenant == d.TenantId));
    }

    [Fact]
    public async Task Real_xmin_token_rejects_stale_checkpoint_update()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); await Process(d, new FixtureOwner());
        await using var a = await pg.OpenAsync(); await using var b = await pg.OpenAsync();
        var left = await a.Set<InboxCheckpointV1>().SingleAsync(x => x.TenantId == d.TenantId);
        var right = await b.Set<InboxCheckpointV1>().SingleAsync(x => x.TenantId == d.TenantId);
        Assert.True(left.RowVersion > 0); Assert.Equal(left.RowVersion, right.RowVersion);
        a.Entry(left).Property(x => x.Version).CurrentValue = 2;
        await a.SaveChangesAsync();
        Assert.NotEqual(right.RowVersion, left.RowVersion);
        b.Entry(right).Property(x => x.Version).CurrentValue = 3;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => b.SaveChangesAsync());
    }

    private static PostgresException? FindPg(Exception ex) => ex as PostgresException ?? (ex.InnerException is null ? null : FindPg(ex.InnerException));

    [Fact]
    public async Task Cross_tenant_transport_collision_never_invokes_owner_mutation_on_incoming_aggregate()
    {
        var a = FixtureOwner.Delivery(Guid.NewGuid()); await Process(a, new FixtureOwner());
        var b = FixtureOwner.Delivery(Guid.NewGuid(), message: a.MessageId); var owner = new FixtureOwner();
        var pending = await Process(b, owner);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportIdentityConflict, pending.Decision);
        Assert.Equal(0, owner.Applications);
        await using var db = await pg.OpenAsync();
        Assert.False((await db.Set<Projection>().SingleAsync(x => x.Tenant == a.TenantId)).Drift);
        Assert.False(await db.Set<Observation>().AnyAsync(x => x.Tenant == b.TenantId));
        Assert.Equal("DROP", (await Read(b, pending, owner))!.TransportOutcome);
    }

    [Fact]
    public async Task Actual_unique_violation_is_rolled_back_then_original_reenters_admission()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); await Process(d, owner);
        owner.AfterOwnerWrite = async tx =>
        {
            // Deliberately violate the REAL Owner journal PK, after content admission. No fake provider exception.
            await tx.Context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO fixture.\"Journal\" SELECT * FROM fixture.\"Journal\" WHERE \"Tenant\"={d.TenantId}");
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => Process(d, owner));
        Assert.Equal("23505", error.SqlState); output.WriteLine("real uniqueness failure=" + error.SqlState);
        var evaluations = owner.Evaluations; owner.AfterOwnerWrite = null;
        var p = await Process(d, owner);
        Assert.Equal(evaluations + 1, owner.Evaluations);
        Assert.Equal(PreCheckpointIdentityDecisionV1.TransportDuplicate, p.Decision);
        await using var db = await pg.OpenAsync();
        Assert.Equal(2, await db.Set<Observation>().CountAsync(x => x.Tenant == d.TenantId));
        Assert.Equal(1, await db.Set<BusinessEffect>().CountAsync(x => x.Tenant == d.TenantId));
    }

    [Fact]
    public async Task Conflict_write_set_rolls_back_together_with_published_health_and_original_bytes()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); var p = await Process(d, owner);
        var original = await Read(d, p, owner);
        var changed = FixtureOwner.Delivery(d.TenantId, d.AggregateId, value: 99);
        owner.FailAfterSave = true;
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Process(changed, owner));
        await using var db = await pg.OpenAsync();
        Assert.False((await db.Set<Projection>().SingleAsync(x => x.Tenant == d.TenantId)).Drift);
        Assert.True((await db.Set<Published>().SingleAsync(x => x.Tenant == d.TenantId)).Allow);
        Assert.Equal(1, await db.Set<Observation>().CountAsync(x => x.Tenant == d.TenantId));
        Assert.Equal(1, await db.Set<InboxDeliveryOutcomeV1>().CountAsync(x => x.TenantId == d.TenantId));
        Assert.Equal(original!.VisibleResult.ToArray(), (await Read(d, p, owner))!.VisibleResult.ToArray());
    }

    [Fact]
    public async Task Final_write_denial_and_cancellation_do_not_create_success_receipts()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid());
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => Process(d, new FixtureOwner { AllowWrite = false }));
        var identity = new TestIdentity(d.TenantId);
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CallerTransactionInboxV1(identity.Accessor, new FixtureOwner())
                .ProcessWithPreCheckpointV1(db, tx.GetDbTransaction(), d, cancellation.Token));
        });
        await AssertEmpty(d.TenantId);
    }

    [Fact]
    public async Task Lost_entire_response_recovers_by_original_key_only_and_cannot_ack_a_delivery()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); await Process(d, owner);
        owner.AllowWrite = false;
        var identity = new TestIdentity(d.TenantId);
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
            var processor = new CallerTransactionInboxV1(identity.Accessor, owner);
            var recovered = await processor.ReadOriginalOperationAsync(db, tx.GetDbTransaction(), "fixture-owner", FixtureOwner.Key(d), "1",
                "fixture-owner-input", "fixture-canonical-v1", FixtureOwner.InputDigest(d), owner);
            Assert.Equal(FixtureOwner.Key(d), recovered!.OperationKey);
            Assert.Equal((await db.Set<Journal>().SingleAsync(x => x.Tenant == d.TenantId)).Result, recovered.VisibleResult.ToArray());
            owner.AllowRead = false;
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => processor.ReadOriginalOperationAsync(db, tx.GetDbTransaction(), "fixture-owner", FixtureOwner.Key(d), "1",
                "fixture-owner-input", "fixture-canonical-v1", FixtureOwner.InputDigest(d), owner));
        });
        Assert.Equal(1, owner.Applications);
    }

    [Fact]
    public async Task Actual_serialization_failure_preserves_original_version_key_on_retry()
    {
        var d1 = FixtureOwner.Delivery(Guid.NewGuid()); await Process(d1, new FixtureOwner());
        var d2 = FixtureOwner.Delivery(d1.TenantId, d1.AggregateId, version: 2);
        var d3 = FixtureOwner.Delivery(d1.TenantId, d1.AggregateId, version: 3);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = Process(d2, new FixtureOwner { AfterAuthorityRead = async _ => { held.SetResult(); await release.Task; } });
        await held.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var owner = new FixtureOwner(); var second = Process(d3, owner);
        await using (var monitor = new NpgsqlConnection(pg.RuntimeConnection))
        {
            await monitor.OpenAsync(); var deadline = DateTime.UtcNow.AddSeconds(10); var blocked = false;
            while (DateTime.UtcNow < deadline)
            {
                blocked = (bool)(await new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND NOT granted)", monitor).ExecuteScalarAsync())!;
                if (blocked) break; await Task.Delay(20);
            }
            Assert.True(blocked);
        }
        release.SetResult(); await first;
        var failure = await Assert.ThrowsAnyAsync<Exception>(() => second);
        Assert.Equal("40001", FindPg(failure)?.SqlState);
        output.WriteLine("real serialization failure=40001; original retry key=" + FixtureOwner.Key(d3));
        var before = owner.Evaluations;
        var recovered = await Process(d3, owner);
        Assert.Equal(before + 1, owner.Evaluations);
        Assert.Equal(PreCheckpointIdentityDecisionV1.FreshCandidate, recovered.Decision);
        await using var db = await pg.OpenAsync();
        Assert.Equal(3, (await db.Set<Projection>().SingleAsync(x => x.Tenant == d1.TenantId)).Version);
        Assert.Equal(3, await db.Set<BusinessEffect>().CountAsync(x => x.Tenant == d1.TenantId));
        Assert.Single(await db.Set<Journal>().Where(x => x.Tenant == d1.TenantId && x.Key == FixtureOwner.Key(d3)).ToArrayAsync());
    }

    [Fact]
    public async Task Two_contexts_can_participate_only_when_the_actual_connection_and_transaction_are_shared()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var identity = new TestIdentity(d.TenantId); var owner = new FixtureOwner();
        PendingCommitResult? pending = null;
        await identity.Run(async () =>
        {
            await using var a = await pg.OpenAsync(); await using var tx = await a.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await using var b = new TestDb(pg.RuntimeConnection, (NpgsqlConnection)a.Database.GetDbConnection());
            await b.Database.UseTransactionAsync(tx.GetDbTransaction());
            pending = await new CallerTransactionInboxV1(identity.Accessor, owner).ProcessWithPreCheckpointV1(b, tx.GetDbTransaction(), d);
            Assert.Null(await Read(d, pending, owner));
            await tx.CommitAsync();
        });
        Assert.Equal("SUCCESS", (await Read(d, pending!, owner))!.TransportOutcome);
    }

    [Fact]
    public async Task Read_confirmation_rejects_write_transaction_and_missing_trusted_context()
    {
        var d = FixtureOwner.Delivery(Guid.NewGuid()); var owner = new FixtureOwner(); var pending = await Process(d, owner);
        var identity = new TestIdentity(d.TenantId);
        await identity.Run(async () =>
        {
            await using var db = await pg.OpenAsync(); await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
            await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => new CallerTransactionInboxV1(identity.Accessor, owner).ReadOriginalResultAsync(db, tx.GetDbTransaction(),
                "fixture-owner", FixtureOwner.Key(d), "1", "fixture-owner-input", "fixture-canonical-v1", FixtureOwner.InputDigest(d), pending.ReceiptId, owner));
        });
        await using var bare = await pg.OpenAsync(); await using var transaction = await bare.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await Assert.ThrowsAsync<InboxProtocolExceptionV1>(() => new CallerTransactionInboxV1(identity.Accessor, owner)
            .ProcessWithPreCheckpointV1(bare, transaction.GetDbTransaction(), d));
    }
}
