using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using CP6.Platform.EntityFramework;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace CP6.Platform.WP0208.PostgreSqlTests;

[Collection("real-pg")]
public sealed class OutboxTransactionTests(PgFixture fixture)
{
    private async Task Scenario(int rows, Func<FixtureOwner, CallerTransactionOutboxV2, Guid[], Task> body)
    {
        var identity = new TestIdentity(Guid.NewGuid());
        await identity.Run(async () =>
        {
            var owner = new FixtureOwner(fixture, new("fixture-owner", identity.Accessor.CurrentTrusted!.Context.TenantId, "fixture-queue", Guid.NewGuid().ToString("N")));
            owner.Trusted = identity.Accessor;
            var ids = await owner.Seed(rows);
            await body(owner, new(identity.Accessor, owner), ids);
        });
    }
    private async Task<T> Write<T>(FixtureOwner owner, CallerTransactionOutboxV2 api, Func<OutboxTransactionV2, Task<T>> action, bool commit = true)
    {
        await using var db = await fixture.OpenAsync();
        await using var native = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var tx = await api.BindTransactionAsync(db, native.GetDbTransaction(), owner.Root);
        var value = await action(tx);
        if (commit) await native.CommitAsync(); else await native.RollbackAsync();
        return value;
    }
    private async Task<T> Read<T>(FixtureOwner owner, CallerTransactionOutboxV2 api, Func<OutboxTransactionV2, Task<T>> action)
    {
        await using var db = await fixture.OpenAsync();
        await using var native = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY");
        var tx = await api.BindTransactionAsync(db, native.GetDbTransaction(), owner.Root, true);
        return await action(tx);
    }
    private async Task<CommittedClaimTicketV2> Claim(FixtureOwner owner, CallerTransactionOutboxV2 api)
    {
        var request = Guid.NewGuid();
        var prepared = (await Write(owner, api, tx => api.ClaimBatchV2(tx, request, 1))).Prepared!;
        return Assert.Single((await Read(owner, api, tx => api.QueryClaimV2(tx, request, prepared.Input))).Committed!);
    }
    private async Task<string> Snapshot(FixtureOwner owner)
    {
        await using var db = await fixture.OpenAsync();
        var root = owner.Root.Id;
        var ids = await db.Set<OutboxLeaseBindingV2>().Where(x => x.RootId == root).Select(x => x.NativeId).ToArrayAsync();
        var messages = await db.Set<Cp6OutboxMessage>().Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).AsNoTracking().ToArrayAsync();
        return JsonSerializer.Serialize(new
        {
            Native = messages,
            Binding = await db.Set<OutboxLeaseBindingV2>().Where(x => x.RootId == root).OrderBy(x => x.NativeId).AsNoTracking().ToArrayAsync(),
            Claims = await db.Set<OutboxClaimReceiptV2>().Where(x => x.RootId == root).OrderBy(x => x.RequestId).AsNoTracking().ToArrayAsync(),
            ClaimTickets = await db.Set<OutboxClaimTicketReceiptV2>().Where(x => x.RootId == root).OrderBy(x => x.RequestId).ThenBy(x => x.NativeId).AsNoTracking().ToArrayAsync(),
            Mutations = await db.Set<OutboxLeaseMutationReceiptV2>().Where(x => x.RootId == root).OrderBy(x => x.MutationId).AsNoTracking().ToArrayAsync(),
            SendAttempts = await db.Set<OutboxSendAttemptV2>().Where(x => x.RootId == root).OrderBy(x => x.ClaimId).AsNoTracking().ToArrayAsync(),
            Audit = await db.Set<OutboxAuditV2>().Where(x => x.RootId == root).OrderBy(x => x.Id).AsNoTracking().ToArrayAsync(),
            Seals = await db.Set<OutboxTokenSealV2>().Where(x => ids.Contains(x.NativeId)).OrderBy(x => x.NativeId).ThenBy(x => x.WriterTransaction).AsNoTracking().ToArrayAsync(),
            Dlq = await db.Set<Cp6DeadLetterRecord>().Where(x => x.TenantId == owner.Root.TenantId).OrderBy(x => x.Id).AsNoTracking().ToArrayAsync()
        });
    }

    [Fact]
    public Task Prepared_claim_is_invisible_until_commit_and_rollback_restores_complete_graph() => Scenario(1, async (owner, api, ids) =>
    {
        var before = await Snapshot(owner);
        var key = Guid.NewGuid(); string input = "";
        await Write(owner, api, async tx =>
        {
            var prepared = await api.ClaimBatchV2(tx, key, 1);
            Assert.Equal(OutboxDecisionV2.PendingCommit, prepared.Decision); Assert.Null(prepared.Committed);
            input = prepared.Prepared!.Input;
            Assert.Equal(OutboxDecisionV2.OutcomeUnknown, (await Read(owner, api, read => api.QueryClaimV2(read, key, input))).Decision);
            return 0;
        }, false);
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(OutboxDecisionV2.OutcomeUnknown, (await Read(owner, api, tx => api.QueryClaimV2(tx, key, input))).Decision);
        var committed = await Claim(owner, api);
        Assert.Equal(ids[0], committed.Ticket.NativeId);
        Assert.Equal(OutboxDecisionV2.Committed, await Write(owner, api, tx => api.ValidateSendV2(tx, committed)));
    });

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public Task Same_claim_key_replays_exact_tuple_and_changed_tuple_has_empty_write_set(int count) => Scenario(count, async (owner, api, ids) =>
    {
        var key = Guid.NewGuid();
        var first = (await Write(owner, api, tx => api.ClaimBatchV2(tx, key, 1))).Prepared!;
        var before = await Snapshot(owner);
        var replay = await Write(owner, api, tx => api.ClaimBatchV2(tx, key, 1));
        Assert.Equal(first.Input, replay.Prepared!.Input); Assert.Equal(first.Tickets, replay.Prepared.Tickets);
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(OutboxDecisionV2.Conflict, (await Write(owner, api, tx => api.ClaimBatchV2(tx, key, 2))).Decision);
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(count, (await Read(owner, api, tx => api.QueryClaimV2(tx, key, first.Input))).Committed!.Count);
    });

    [Fact]
    public Task Ticket_uses_actual_post_write_xmin_and_native_and_binding_are_sealed() => Scenario(1, async (owner, api, ids) =>
    {
        await Write(owner, api, async tx =>
        {
            var t = Assert.Single((await api.ClaimBatchV2(tx, Guid.NewGuid(), 1)).Prepared!.Tickets);
            var n = await tx.Context.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == ids[0]);
            var b = await tx.Context.Set<OutboxLeaseBindingV2>().AsNoTracking().SingleAsync(x => x.NativeId == ids[0]);
            var receipt = await tx.Context.Set<OutboxClaimTicketReceiptV2>().AsNoTracking().SingleAsync(x => x.RootId == owner.Root.Id && x.RequestId == t.ClaimRequestId && x.NativeId == ids[0]);
            Assert.Equal(t.ClaimReceiptRef, receipt.ReceiptReference);
            Assert.Equal(t, JsonSerializer.Deserialize<ClaimTicketV2>(receipt.Ticket));
            Assert.Equal(tx.TransactionId, receipt.WriterTransaction);
            Assert.Equal(OutboxEncodingV2.Encode(OutboxEncodingV2.NativeValue(n.RowVersion)), t.ExpectedNativeVersion);
            Assert.Equal(OutboxEncodingV2.Encode(b.Version), t.ExpectedBindingVersion);
            await using var cmd = tx.Transaction.Connection!.CreateCommand(); cmd.Transaction = tx.Transaction;
            cmd.CommandText = "SELECT xmin::text FROM fixture.\"Cp6_OutboxMessage\" WHERE \"Id\"=@id";
            var p = cmd.CreateParameter(); p.ParameterName = "id"; p.Value = ids[0]; cmd.Parameters.Add(p);
            Assert.Equal("pg-xmin-v1:" + await cmd.ExecuteScalarAsync(), t.ExpectedNativeVersion);
            await tx.Context.Database.CurrentTransaction!.CreateSavepointAsync("sealed_native");
            var error = await Assert.ThrowsAsync<PostgresException>(() => tx.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE fixture.\"Cp6_OutboxMessage\" SET \"AttemptCount\"=99 WHERE \"Id\"={ids[0]}"));
            Assert.Equal("55000", error.SqlState); Assert.Contains("TOKEN_ALREADY_RECORDED", error.MessageText);
            await tx.Context.Database.CurrentTransaction!.RollbackToSavepointAsync("sealed_native");
            error = await Assert.ThrowsAsync<PostgresException>(() => tx.Context.Database.ExecuteSqlInterpolatedAsync($"UPDATE fixture.\"Cp6_OutboxBindingV2\" SET \"AssignedEpoch\"=2 WHERE \"NativeId\"={ids[0]}"));
            Assert.Equal("55000", error.SqlState);
            await tx.Context.Database.CurrentTransaction!.RollbackToSavepointAsync("sealed_native");
            return 0;
        });
    });

    [Fact]
    public Task Dlq_mutation_rolls_back_native_binding_receipt_dlq_audit_and_seal() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api); var before = await Snapshot(owner);
        var input = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.RecordPublishFailure, "fixture-policy-v1", "FIXTURE_PUBLISH_FAILED");
        await Write(owner, api, async tx =>
        {
            Assert.Equal("DeadLettered", (await api.CompleteClaimV2(tx, claim, input)).Transition);
            Assert.Single(await tx.Context.Set<Cp6DeadLetterRecord>().Where(x => x.TenantId == owner.Root.TenantId).ToArrayAsync());
            return 0;
        }, false);
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(OutboxDecisionV2.OutcomeUnknown, (await Read(owner, api, tx => api.QueryClaimMutationV2(tx, input.MutationId, CallerTransactionOutboxV2.MutationInput(input)))).Decision);
    });

    [Fact]
    public Task Real_commit_then_suppressed_ack_recovers_original_mutation_after_write_revocation() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        var input = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1");
        // Fault injection is after the real PostgreSQL COMMIT, at the application acknowledgement boundary.
        await Assert.ThrowsAsync<LostAcknowledgement>(async () =>
        {
            await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, input));
            throw new LostAcknowledgement();
        });
        await owner.SetAuthority(a => a.Write = false);
        var before = await Snapshot(owner);
        var result = await Read(owner, api, tx => api.QueryClaimMutationV2(tx, input.MutationId, CallerTransactionOutboxV2.MutationInput(input)));
        Assert.Equal(OutboxDecisionV2.Committed, result.Decision); Assert.Equal("Published", result.Transition);
        Assert.Equal(OutboxDecisionV2.Conflict, (await Read(owner, api, tx => api.QueryClaimMutationV2(tx, input.MutationId, CallerTransactionOutboxV2.MutationInput(input with { Intent = OutboxIntentV2.RecordPublishFailure })))).Decision);
        Assert.Equal(OutboxDecisionV2.PendingCommit, (await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, input))).Decision);
        Assert.Equal(before, await Snapshot(owner));
        var denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1)));
        Assert.Equal("FIXTURE_WRITE_DENIED", denied.Code);
        await owner.SetAuthority(a => a.Read = false);
        denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Read(owner, api, tx => api.QueryClaimMutationV2(tx, input.MutationId, CallerTransactionOutboxV2.MutationInput(input))));
        Assert.Equal("FIXTURE_READ_DENIED", denied.Code);
    });
    private sealed class LostAcknowledgement : Exception;

    [Fact]
    public Task Renewal_mints_post_write_ticket_and_old_worker_ticket_loses_authority() => Scenario(1, async (owner, api, ids) =>
    {
        var original = await Claim(owner, api);
        var input = new OutboxRenewalV2(Guid.NewGuid(), original.Ticket, "fixture-policy-v1", TimeSpan.FromSeconds(60));
        var prepared = await Write(owner, api, tx => api.RenewClaimV2(tx, original, input));
        Assert.Equal(OutboxDecisionV2.PendingCommit, prepared.Decision);
        var renewed = await Read(owner, api, tx => api.QueryRenewedTicketV2(tx, input)); Assert.NotNull(renewed);
        Assert.NotEqual(original.Ticket.ExpectedNativeVersion, renewed.Ticket.ExpectedNativeVersion);
        Assert.NotEqual(original.Ticket.ExpectedBindingVersion, renewed.Ticket.ExpectedBindingVersion);
        Assert.True(renewed.Ticket.LeaseExpiresAtUtc > original.Ticket.LeaseExpiresAtUtc);
        Assert.Equal(OutboxDecisionV2.LeaseLost, await Write(owner, api, tx => api.ValidateSendV2(tx, original)));
        Assert.Equal(OutboxDecisionV2.Committed, await Write(owner, api, tx => api.ValidateSendV2(tx, renewed)));
        var before = await Snapshot(owner);
        Assert.Equal(OutboxDecisionV2.PendingCommit, (await Write(owner, api, tx => api.RenewClaimV2(tx, original, input))).Decision);
        Assert.Equal(before, await Snapshot(owner));
    });

    [Theory]
    [InlineData("native", OutboxDecisionV2.Conflict)]
    [InlineData("binding", OutboxDecisionV2.Conflict)]
    [InlineData("hash", OutboxDecisionV2.Conflict)]
    [InlineData("tenant", OutboxDecisionV2.Conflict)]
    [InlineData("epoch", OutboxDecisionV2.RouteEpochChanged)]
    public Task Current_native_binding_hash_tenant_and_epoch_are_rechecked(string tamper, OutboxDecisionV2 expected) => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        await Write(owner, api, async tx =>
        {
            // An authorized fixture writer deliberately creates drift under the shared locks.
            await tx.Context.Database.ExecuteSqlInterpolatedAsync($"SELECT set_config('cp6.outbox_v2_writer', {tx.TransactionId + ":" + owner.Root.Id}, true)");
            var sql = tamper switch
            {
                "native" => "UPDATE fixture.\"Cp6_OutboxMessage\" SET \"AttemptCount\"=\"AttemptCount\"+1 WHERE \"Id\"={0}",
                "binding" => "UPDATE fixture.\"Cp6_OutboxBindingV2\" SET \"AssignedEpoch\"=\"AssignedEpoch\" WHERE \"NativeId\"={0}",
                "hash" => "UPDATE fixture.\"Cp6_OutboxMessage\" SET \"Payload\"=decode('00','hex') WHERE \"Id\"={0}",
                "tenant" => "UPDATE fixture.\"Cp6_OutboxMessage\" SET \"TenantId\"='00000000-0000-0000-0000-000000000001' WHERE \"Id\"={0}",
                _ => "UPDATE fixture.\"Cp6_OutboxBindingV2\" SET \"AssignedEpoch\"=2 WHERE \"NativeId\"={0}"
            };
            await tx.Context.Database.ExecuteSqlRawAsync(sql, ids[0]); return 0;
        });
        var before = await Snapshot(owner);
        var input = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1");
        Assert.Equal(expected, (await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, input))).Decision);
        Assert.Equal(before, await Snapshot(owner));
    });

    [Fact]
    public Task Actual_23505_aborts_transaction_and_original_key_retry_returns_original_ticket() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        await using var db = await fixture.OpenAsync();
        var original = await db.Set<OutboxClaimReceiptV2>().AsNoTracking().SingleAsync(x => x.RootId == owner.Root.Id);
        var before = await Snapshot(owner);
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => Write(owner, api, async tx =>
        {
            tx.Context.Add(new OutboxClaimReceiptV2 { RootId = original.RootId, RequestId = original.RequestId, Input = original.Input, Tickets = original.Tickets, WriterTransaction = tx.TransactionId });
            await tx.Context.SaveChangesAsync(); return 0;
        }));
        Assert.Equal("23505", Assert.IsType<PostgresException>(ex.InnerException).SqlState);
        var replay = await Write(owner, api, tx => api.ClaimBatchV2(tx, original.RequestId, 1));
        Assert.Equal(claim.Ticket, Assert.Single(replay.Prepared!.Tickets)); Assert.Equal(before, await Snapshot(owner));
    });

    [Fact]
    public Task Concurrent_serializable_claim_loser_gets_40001_then_original_request_can_reenter() => Scenario(1, async (owner, api, ids) =>
    {
        await using var firstDb = await fixture.OpenAsync(); await using var secondDb = await fixture.OpenAsync();
        await using var firstNative = await firstDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var secondNative = await secondDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var first = await api.BindTransactionAsync(firstDb, firstNative.GetDbTransaction(), owner.Root);
        // Establish second transaction's real PG snapshot before the first writes.
        await secondDb.Set<OutboxRouteControlV2>().AsNoTracking().SingleAsync(x => x.Id == owner.Root.Id);
        var request = Guid.NewGuid(); var prepared = await api.ClaimBatchV2(first, request, 1);
        var waiter = api.BindTransactionAsync(secondDb, secondNative.GetDbTransaction(), owner.Root);
        await firstNative.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            var second = await waiter; await api.ClaimBatchV2(second, request, 1);
        });
        Assert.Equal("40001", error.SqlState); await secondNative.RollbackAsync();
        var before = await Snapshot(owner);
        var replay = await Write(owner, api, tx => api.ClaimBatchV2(tx, request, 1));
        Assert.Equal(prepared.Prepared!.Tickets, replay.Prepared!.Tickets); Assert.Equal(before, await Snapshot(owner));
    });

    [Fact]
    public Task Missing_root_and_wrong_mapping_fail_closed() => Scenario(0, async (owner, api, ids) =>
    {
        owner.Mapping = "unproven";
        var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1)));
        Assert.Equal("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED", error.Code); owner.Mapping = OutboxEncodingV2.Mapping;
        await using (var db = await fixture.OpenAsync()) await db.Set<OutboxRouteControlV2>().Where(x => x.Id == owner.Root.Id).ExecuteDeleteAsync();
        Assert.Equal(OutboxDecisionV2.RouteUnavailable, (await Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1))).Decision);
    });

    private async Task<OutboxRouteControlV2> Root(FixtureOwner owner)
    {
        await using var db = await fixture.OpenAsync();
        return await db.Set<OutboxRouteControlV2>().AsNoTracking().SingleAsync(x => x.Id == owner.Root.Id);
    }
    private async Task<OutboxRetentionCandidateV1> Candidate(FixtureOwner owner, Guid id)
    {
        await using var db = await fixture.OpenAsync();
        var n = await db.Set<Cp6OutboxMessage>().AsNoTracking().SingleAsync(x => x.Id == id);
        return new(id, OutboxEncodingV2.Encode(OutboxEncodingV2.NativeValue(n.RowVersion)), "fixture-retention", "fixture-policy-v1");
    }

    [Fact]
    public Task Drain_denies_claim_and_renew_but_allows_current_completion_then_epoch_advance() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api); var before = await Root(owner); var switchId = Guid.NewGuid();
        Assert.Equal(OutboxDecisionV2.PendingCommit, (await Write(owner, api, tx => api.BeginDrainV2(tx, switchId, 1, OutboxEncodingV2.Encode(before.Version)))).Decision);
        Assert.Equal(OutboxPhaseV2.Draining, (await Root(owner)).Phase);
        Assert.Equal(OutboxDecisionV2.RouteUnavailable, (await Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1))).Decision);
        var renewal = new OutboxRenewalV2(Guid.NewGuid(), claim.Ticket, "fixture-policy-v1", TimeSpan.FromSeconds(60));
        Assert.Equal(OutboxDecisionV2.RouteUnavailable, (await Write(owner, api, tx => api.RenewClaimV2(tx, claim, renewal))).Decision);
        var draining = await Root(owner);
        Assert.Equal(OutboxDecisionV2.Retained, (await Write(owner, api, tx => api.AdvanceEpochV2(tx, Guid.NewGuid(), switchId, 1, OutboxEncodingV2.Encode(draining.Version)))).Decision);
        var completion = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1");
        Assert.Equal("Published", (await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, completion))).Transition);
        var advance = await Write(owner, api, tx => api.AdvanceEpochV2(tx, Guid.NewGuid(), switchId, 1, OutboxEncodingV2.Encode(draining.Version)));
        Assert.Equal(2, advance.Epoch); Assert.Equal(OutboxDecisionV2.PendingCommit, advance.Decision);
        Assert.Equal(OutboxPhaseV2.Active, (await Root(owner)).Phase);
        Assert.Equal(OutboxDecisionV2.RouteEpochChanged, await Write(owner, api, tx => api.ValidateSendV2(tx, claim)));
        Assert.Equal(OutboxDecisionV2.Committed, (await Read(owner, api, tx => api.QueryClaimMutationV2(tx, completion.MutationId, CallerTransactionOutboxV2.MutationInput(completion)))).Decision);
    });

    [Fact]
    public Task Unknown_handover_blocks_epoch_and_caller_rollback_restores_root_and_history() => Scenario(1, async (owner, api, ids) =>
    {
        var root = await Root(owner); var switchId = Guid.NewGuid();
        await Write(owner, api, tx => api.BeginDrainV2(tx, switchId, 1, OutboxEncodingV2.Encode(root.Version)));
        var draining = await Root(owner); await owner.SetAuthority(a => a.UnknownResolved = false);
        var before = await Snapshot(owner);
        await Write(owner, api, async tx =>
        {
            Assert.Equal(OutboxDecisionV2.PendingCommit, (await api.AdvanceEpochV2(tx, Guid.NewGuid(), switchId, 1, OutboxEncodingV2.Encode(draining.Version))).Decision);
            Assert.Equal(OutboxPhaseV2.Blocked, (await tx.Context.Set<OutboxRouteControlV2>().AsNoTracking().SingleAsync(x => x.Id == owner.Root.Id)).Phase);
            return 0;
        }, false);
        Assert.Equal(OutboxPhaseV2.Draining, (await Root(owner)).Phase); Assert.Equal(before, await Snapshot(owner));
        await owner.SetAuthority(a => a.UnknownResolved = true);
        await Write(owner, api, tx => api.AdvanceEpochV2(tx, Guid.NewGuid(), switchId, 1, OutboxEncodingV2.Encode(draining.Version)));
        await using var db = await fixture.OpenAsync();
        Assert.Equal(2, (await db.Set<OutboxLeaseBindingV2>().SingleAsync(x => x.NativeId == ids[0])).AssignedEpoch);
    });

    [Fact]
    public Task Committed_blocked_epoch_recovers_original_mutation_after_lost_acknowledgement() => Scenario(1, async (owner, api, ids) =>
    {
        var root = await Root(owner); var switchId = Guid.NewGuid(); var mutationId = Guid.NewGuid();
        await Write(owner, api, tx => api.BeginDrainV2(tx, switchId, 1, OutboxEncodingV2.Encode(root.Version)));
        var expectedControlVersion = OutboxEncodingV2.Encode((await Root(owner)).Version);
        await owner.SetAuthority(a => a.UnknownResolved = false);
        OutboxSwitchResultV2? original = null;
        await Assert.ThrowsAsync<LostAcknowledgement>(async () =>
        {
            original = await Write(owner, api, tx => api.AdvanceEpochV2(tx, mutationId, switchId, 1, expectedControlVersion));
            throw new LostAcknowledgement(); // The real PostgreSQL COMMIT already succeeded.
        });
        Assert.Equal(OutboxDecisionV2.PendingCommit, original!.Decision);
        Assert.Equal(OutboxPhaseV2.Blocked, (await Root(owner)).Phase);
        var before = await Snapshot(owner);
        var replay = await Write(owner, api, tx => api.AdvanceEpochV2(tx, mutationId, switchId, 1, expectedControlVersion));
        Assert.Equal(original, replay);
        Assert.Equal(before, await Snapshot(owner));
        await Read(owner, api, async tx =>
        {
            var receipt = await tx.Context.Set<OutboxLeaseMutationReceiptV2>().AsNoTracking()
                .SingleAsync(x => x.RootId == owner.Root.Id && x.MutationId == mutationId);
            Assert.NotEqual(tx.TransactionId, receipt.WriterTransaction);
            Assert.Equal(JsonSerializer.Serialize(new { Kind = "AdvanceEpoch", mutationId, switchId, expectedEpoch = 1L, expectedControlVersion }), receipt.Input);
            Assert.Equal(original, JsonSerializer.Deserialize<OutboxSwitchResultV2>(receipt.Output));
            return 0;
        });
        Assert.Equal(OutboxDecisionV2.Conflict,
            (await Write(owner, api, tx => api.AdvanceEpochV2(tx, mutationId, switchId, 2, expectedControlVersion))).Decision);
        Assert.Equal(before, await Snapshot(owner));
        await owner.SetAuthority(a => a.UnknownResolved = true);
        Assert.Equal(original, await Write(owner, api, tx => api.AdvanceEpochV2(tx, mutationId, switchId, 1, expectedControlVersion)));
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(OutboxPhaseV2.Blocked, (await Root(owner)).Phase);
        Assert.Equal(2, (await Write(owner, api, tx => api.AdvanceEpochV2(tx, Guid.NewGuid(), switchId, 1, original.ControlVersion!))).Epoch);
    });

    [Theory]
    [InlineData("hold", OutboxDecisionV2.Retained)]
    [InlineData("reference", OutboxDecisionV2.Retained)]
    [InlineData("consumer", OutboxDecisionV2.Retained)]
    [InlineData("unknown", OutboxDecisionV2.Retained)]
    [InlineData("policy", OutboxDecisionV2.PolicyUnavailable)]
    public Task Exact_retention_checks_current_owner_obligations(string reason, OutboxDecisionV2 decision) => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, new(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1")));
        var candidate = await Candidate(owner, ids[0]);
        await owner.SetAuthority(a =>
        {
            if (reason == "hold") a.Hold = true;
            if (reason == "reference") a.ReferenceClosed = false;
            if (reason == "consumer") a.ConsumersComplete = false;
            if (reason == "unknown") a.UnknownResolved = false;
            if (reason == "policy") a.PolicyAvailable = false;
        });
        var before = await Snapshot(owner);
        var result = await Write(owner, api, tx => api.DeleteEligibleInTransactionV1(tx, [candidate]));
        Assert.Equal(decision, result.Decision); Assert.Equal(ids, result.Retained); Assert.Empty(result.Deleted);
        Assert.Equal(before, await Snapshot(owner));
    });

    [Fact]
    public Task Retention_rolls_back_exact_graph_then_commits_deletion_with_original_receipts_preserved() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        var completion = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.RecordPublishFailure, "fixture-policy-v1", "FIXTURE_TERMINAL");
        await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, completion));
        var candidate = await Candidate(owner, ids[0]); var before = await Snapshot(owner);
        var result = await Write(owner, api, tx => api.DeleteEligibleInTransactionV1(tx, [candidate]), false);
        Assert.Equal(ids, result.Deleted); Assert.Equal(before, await Snapshot(owner));
        result = await Write(owner, api, tx => api.DeleteEligibleInTransactionV1(tx, [candidate]));
        Assert.Equal(ids, result.Deleted);
        await using var db = await fixture.OpenAsync();
        Assert.False(await db.Set<Cp6OutboxMessage>().AnyAsync(x => x.Id == ids[0]));
        Assert.False(await db.Set<OutboxLeaseBindingV2>().AnyAsync(x => x.NativeId == ids[0]));
        Assert.False(await db.Set<Cp6DeadLetterRecord>().AnyAsync(x => x.TenantId == owner.Root.TenantId));
        Assert.True(await db.Set<OutboxAuditV2>().AnyAsync(x => x.RootId == owner.Root.Id && x.Kind == "RetentionDeleted"));
        Assert.True(await db.Set<OutboxTokenSealV2>().AnyAsync(x => x.NativeId == ids[0]));
        Assert.Equal(OutboxDecisionV2.Committed, (await Read(owner, api, tx => api.QueryClaimMutationV2(tx, completion.MutationId, CallerTransactionOutboxV2.MutationInput(completion)))).Decision);
    });

    [Fact]
    public Task Every_legacy_writer_excludes_bound_rows_while_unbound_claim_complete_and_retention_work() => Scenario(1, async (owner, api, ids) =>
    {
        var options = new Cp6TransactionalMessagingOptions();
        await using var db = await fixture.OpenAsync();
        var store = new Cp6OutboxStore<TestDb>(db, new AcceptEnvelope());
        var unbound = store.Enqueue(new(Guid.NewGuid().ToString("N"), owner.Root.TenantId, "fixture-topic", "fixture-partition",
            new byte[] { 123, 125 }, "fixture-correlation", "fixture-causation", "fixture-unbound", 1));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var oldClaims = await store.ClaimBatchAsync("legacy-fixture-worker", options);
        var oldClaim = Assert.Single(oldClaims); Assert.Equal(unbound.Id, oldClaim.Message.Id);
        await store.MarkPublishedAsync(oldClaim); db.ChangeTracker.Clear();

        var current = await Claim(owner, api);
        var boundClaim = new Cp6ClaimedOutboxMessage(current.Message, current.Ticket.LeaseOwner, current.Ticket.LeaseToken, current.Ticket.LeaseExpiresAtUtc);
        var before = await Snapshot(owner);
        var denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => store.MarkPublishedAsync(boundClaim));
        Assert.Equal("CP6_OUTBOX_V2_LEGACY_BYPASS", denied.Code);
        denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => store.MarkFailedAsync(boundClaim, "FIXTURE_FAILURE", true, options));
        Assert.Equal("CP6_OUTBOX_V2_LEGACY_BYPASS", denied.Code);
        denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => store.MarkFailedAsync(boundClaim, "FIXTURE_FAILURE", false, options));
        Assert.Equal("CP6_OUTBOX_V2_LEGACY_BYPASS", denied.Code); Assert.Equal(before, await Snapshot(owner));

        await Write(owner, api, tx => api.CompleteClaimV2(tx, current, new(Guid.NewGuid(), current.Ticket, OutboxIntentV2.RecordPublishFailure, "fixture-policy-v1", "FIXTURE_TERMINAL")));
        before = await Snapshot(owner);
        denied = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => store.RequeueDeadLetteredAsync(ids[0], "FIXTURE_REPLAY"));
        Assert.Equal("CP6_OUTBOX_V2_LEGACY_BYPASS", denied.Code);
        var retention = await new Cp6MessageRetentionService<TestDb>(db, new FutureClock()).DeleteExpiredAsync(options);
        Assert.Equal(1, retention.OutboxDeleted); Assert.Equal(0, retention.DeadLettersDeleted);
        Assert.Equal(before, await Snapshot(owner));
        Assert.False(await db.Set<Cp6OutboxMessage>().AnyAsync(x => x.Id == unbound.Id));
        Assert.True(await db.Set<Cp6DeadLetterRecord>().AnyAsync(x => x.MessageId == current.Ticket.MessageId));
    });

    [Fact]
    public Task Direct_legacy_DML_cannot_bypass_database_guard() => Scenario(1, async (owner, api, ids) =>
    {
        var current = await Claim(owner, api);
        await Write(owner, api, tx => api.CompleteClaimV2(tx, current, new(Guid.NewGuid(), current.Ticket, OutboxIntentV2.RecordPublishFailure, "fixture-policy-v1", "FIXTURE_TERMINAL")));
        var before = await Snapshot(owner);
        foreach (var sql in new[]
        {
            "UPDATE fixture.\"Cp6_OutboxMessage\" SET \"Status\"=0 WHERE \"Id\"={0}",
            "DELETE FROM fixture.\"Cp6_OutboxMessage\" WHERE \"Id\"={0}",
            "DELETE FROM fixture.\"Cp6_OutboxBindingV2\" WHERE \"NativeId\"={0}",
            "DELETE FROM fixture.\"Cp6_DeadLetterRecord\" WHERE \"MessageId\"=(SELECT \"MessageId\" FROM fixture.\"Cp6_OutboxMessage\" WHERE \"Id\"={0})"
        })
        {
            await using var db = await fixture.OpenAsync();
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(sql, ids[0]));
            Assert.Equal("55000", error.SqlState); Assert.Contains("LEGACY_BYPASS", error.MessageText);
        }
        Assert.Equal(before, await Snapshot(owner));
    });
    private sealed class AcceptEnvelope : ICp6OutboxEnvelopeValidator
    {
        public Cp6MessageValidationResult Validate(Cp6OutboxEnvelope envelope) => Cp6MessageValidationResult.Valid;
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Dispatcher_publishes_without_locks_and_ambiguous_or_unconfirmed_completion_never_republishes(bool transportThrows) => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api); var mutation = Guid.NewGuid();
        var publisher = new ProbePublisher(async message =>
        {
            Assert.Equal(claim.Ticket.MessageId, message.MessageId);
            await using var db = await fixture.OpenAsync();
            var a = await db.Set<Authority>().AsNoTracking().SingleAsync(x => x.RootId == owner.Root.Id);
            await using var cmd = db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND database=(SELECT oid FROM pg_database WHERE datname=current_database()) AND classid::bigint=@hi AND objid::bigint=@lo AND objsubid=1 AND granted";
            var hi = cmd.CreateParameter(); hi.ParameterName = "hi"; hi.Value = (long)(uint)((ulong)a.RouteLock >> 32); cmd.Parameters.Add(hi);
            var lo = cmd.CreateParameter(); lo.ParameterName = "lo"; lo.Value = (long)(uint)a.RouteLock; cmd.Parameters.Add(lo);
            Assert.Equal(0L, await cmd.ExecuteScalarAsync());
            if (transportThrows) throw new IOException("fixture ambiguous transport");
            await owner.SetAuthority(x => x.Write = false);
            return new(OutboxTransportDecisionV2.Confirmed);
        });
        var dispatcher = new OutboxDispatcherV2<TestDb>(new Factory(fixture.RuntimeConnection), owner.Trusted, owner, publisher);
        var observed = await dispatcher.DispatchClaimV2(claim, mutation);
        Assert.Equal("UNCERTAIN_DELIVERY", observed.State); Assert.Equal(mutation, observed.MutationId);
        if (transportThrows) Assert.Null(observed.OriginalMutation);
        else
        {
            Assert.NotNull(observed.OriginalMutation); Assert.Equal(OutboxIntentV2.PublishConfirmed, observed.OriginalMutation.Intent);
            Assert.Equal(OutboxDecisionV2.OutcomeUnknown, (await dispatcher.RecoverMutationV2(observed.OriginalMutation)).Decision);
            await owner.SetAuthority(x => x.Write = true);
        }
        Assert.Equal("SEND_NOT_AUTHORIZED", (await dispatcher.DispatchClaimV2(claim, mutation)).State);
        Assert.Equal("SEND_NOT_AUTHORIZED", (await dispatcher.DispatchClaimV2(claim, Guid.NewGuid())).State);
        Assert.Equal(1, publisher.Calls);
        await using var check = await fixture.OpenAsync();
        var native = await check.Set<Cp6OutboxMessage>().SingleAsync(x => x.Id == ids[0]);
        Assert.Equal(Cp6OutboxStatus.Dispatching, native.Status); Assert.Equal(1, native.AttemptCount);
        Assert.False(await check.Set<OutboxLeaseMutationReceiptV2>().AnyAsync(x => x.RootId == owner.Root.Id));
        Assert.False(await check.Set<Cp6DeadLetterRecord>().AnyAsync(x => x.TenantId == owner.Root.TenantId));
        Assert.Single(await check.Set<OutboxSendAttemptV2>().Where(x => x.RootId == owner.Root.Id).ToArrayAsync());
    });

    [Fact]
    public Task Dispatcher_does_not_publish_after_send_commit_acknowledgement_outlives_lease() => Scenario(1, async (owner, api, ids) =>
    {
        owner.LeaseDuration = TimeSpan.FromSeconds(2);
        var claim = await Claim(owner, api); var mutationId = Guid.NewGuid();
        var publisher = new ProbePublisher(_ => Task.FromResult(new OutboxTransportResultV2(OutboxTransportDecisionV2.Confirmed)));
        var dispatcher = new OutboxDispatcherV2<TestDb>(new DelayedCommitFactory(fixture.RuntimeConnection, TimeSpan.FromSeconds(2.2)), owner.Trusted, owner, publisher);
        var observed = await dispatcher.DispatchClaimV2(claim, mutationId);
        await using var check = await fixture.OpenAsync();
        await using var clock = check.Database.GetDbConnection().CreateCommand();
        clock.CommandText = "SELECT clock_timestamp() >= @expiry";
        var expiry = clock.CreateParameter(); expiry.ParameterName = "expiry"; expiry.Value = claim.Ticket.LeaseExpiresAtUtc; clock.Parameters.Add(expiry);
        Assert.Equal(true, await clock.ExecuteScalarAsync());
        Assert.Equal(0, publisher.Calls);
        Assert.Equal("SEND_NOT_AUTHORIZED", observed.State);
        Assert.Equal(OutboxDecisionV2.Expired, observed.CompletionDecision);
        var attempt = Assert.Single(await check.Set<OutboxSendAttemptV2>().Where(x => x.RootId == owner.Root.Id).ToArrayAsync());
        Assert.Equal(mutationId, attempt.MutationId);
        Assert.Equal(JsonSerializer.Serialize(claim.Ticket), attempt.OriginalTicket);
        var native = await check.Set<Cp6OutboxMessage>().SingleAsync(x => x.Id == ids[0]);
        Assert.Equal(1, native.AttemptCount); Assert.Equal(Cp6OutboxStatus.Dispatching, native.Status);
        Assert.False(await check.Set<OutboxLeaseMutationReceiptV2>().AnyAsync(x => x.RootId == owner.Root.Id));
        Assert.Equal("SEND_NOT_AUTHORIZED", (await dispatcher.DispatchClaimV2(claim, mutationId)).State);
        Assert.Equal(0, publisher.Calls);
    });

    [Fact]
    public Task Dispatcher_deducts_send_commit_latency_from_publish_timeout() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        var publisher = new WaitForCancellationPublisher(() => AssertRouteLockReleased(owner));
        var dispatcher = new OutboxDispatcherV2<TestDb>(new DelayedCommitFactory(fixture.RuntimeConnection, TimeSpan.FromSeconds(2)), owner.Trusted, owner, publisher);
        var observed = await dispatcher.DispatchClaimV2(claim, Guid.NewGuid());
        await publisher.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, publisher.Calls);
        Assert.True(publisher.EntryVerified);
        Assert.Equal("UNCERTAIN_DELIVERY", observed.State);
        // Approved publish budget is five seconds. Two spent awaiting COMMIT cannot be granted again.
        Assert.InRange(publisher.Elapsed.TotalSeconds, 0.01, 4.5);
    });

    [Fact]
    public Task Dispatcher_publishes_after_short_send_commit_delay_within_budget() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        var publisher = new ProbePublisher(async _ =>
        {
            await AssertRouteLockReleased(owner); return new(OutboxTransportDecisionV2.Confirmed);
        });
        var dispatcher = new OutboxDispatcherV2<TestDb>(new DelayedCommitFactory(fixture.RuntimeConnection, TimeSpan.FromMilliseconds(200)), owner.Trusted, owner, publisher);
        var observed = await dispatcher.DispatchClaimV2(claim, Guid.NewGuid());
        Assert.Equal(1, publisher.Calls);
        Assert.Equal("Published", observed.State);
        Assert.Equal(OutboxDecisionV2.Committed, observed.CompletionDecision);
    });

    [Fact]
    public Task Dispatcher_does_not_enter_publisher_when_cancelled_after_send_commit() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api); var mutationId = Guid.NewGuid(); using var cancel = new CancellationTokenSource();
        var publisher = new ProbePublisher(_ => Task.FromResult(new OutboxTransportResultV2(OutboxTransportDecisionV2.Confirmed)));
        var dispatcher = new OutboxDispatcherV2<TestDb>(new DelayedCommitFactory(fixture.RuntimeConnection, TimeSpan.Zero, cancel.Cancel), owner.Trusted, owner, publisher);
        await dispatcher.DispatchClaimV2(claim, mutationId, cancel.Token);
        Assert.Equal(0, publisher.Calls);
        await using var check = await fixture.OpenAsync();
        Assert.Equal(mutationId, (await check.Set<OutboxSendAttemptV2>().SingleAsync(x => x.RootId == owner.Root.Id)).MutationId);
        Assert.False(await check.Set<OutboxLeaseMutationReceiptV2>().AnyAsync(x => x.RootId == owner.Root.Id));
    });

    private async Task AssertRouteLockReleased(FixtureOwner owner)
    {
        await using var db = await fixture.OpenAsync();
        var authority = await db.Set<Authority>().AsNoTracking().SingleAsync(x => x.RootId == owner.Root.Id);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT count(*) FROM pg_locks WHERE locktype='advisory' AND database=(SELECT oid FROM pg_database WHERE datname=current_database()) AND classid::bigint=@hi AND objid::bigint=@lo AND objsubid=1 AND granted";
        var hi = command.CreateParameter(); hi.ParameterName = "hi"; hi.Value = (long)(uint)((ulong)authority.RouteLock >> 32); command.Parameters.Add(hi);
        var lo = command.CreateParameter(); lo.ParameterName = "lo"; lo.Value = (long)(uint)authority.RouteLock; command.Parameters.Add(lo);
        Assert.Equal(0L, await command.ExecuteScalarAsync());
    }

    [Fact]
    public Task Database_wall_clock_expiry_rejects_completion_even_when_transaction_started_before_expiry() => Scenario(1, async (owner, api, ids) =>
    {
        owner.LeaseDuration = TimeSpan.FromSeconds(2);
        var claim = await Claim(owner, api); var before = await Snapshot(owner);
        await Write(owner, api, async tx =>
        {
            await using var cmd = tx.Transaction.Connection!.CreateCommand(); cmd.Transaction = tx.Transaction;
            cmd.CommandText = "SELECT transaction_timestamp() < @expiry";
            var expiry = cmd.CreateParameter(); expiry.ParameterName = "expiry"; expiry.Value = claim.Ticket.LeaseExpiresAtUtc; cmd.Parameters.Add(expiry);
            Assert.Equal(true, await cmd.ExecuteScalarAsync());
            await tx.Context.Database.ExecuteSqlRawAsync("SELECT pg_sleep(2.1)");
            var input = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1");
            Assert.Equal(OutboxDecisionV2.Expired, (await api.CompleteClaimV2(tx, claim, input)).Decision);
            return 0;
        });
        Assert.Equal(before, await Snapshot(owner));
    });
    private sealed class Factory(string connection) : IDbContextFactory<TestDb>
    {
        public TestDb CreateDbContext() => new(connection);
    }
    private sealed class DelayedCommitFactory(string connection, TimeSpan delay, Action? afterCommit = null) : IDbContextFactory<TestDb>
    {
        private readonly DelayedCommitInterceptor _interceptor = new(delay, afterCommit);
        public TestDb CreateDbContext() => new DelayedCommitDb(connection, _interceptor);
    }
    private sealed class DelayedCommitDb(string connection, DbTransactionInterceptor interceptor) : TestDb(connection)
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            base.OnConfiguring(options); options.AddInterceptors(interceptor);
        }
    }
    private sealed class DelayedCommitInterceptor(TimeSpan delay, Action? afterCommit) : DbTransactionInterceptor
    {
        private int _commits;
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            // This callback runs after the real PostgreSQL COMMIT; only its acknowledgement is delayed.
            if (Interlocked.Increment(ref _commits) == 1)
            {
                await Task.Delay(delay, cancellationToken); afterCommit?.Invoke();
            }
        }
    }
    private sealed class WaitForCancellationPublisher(Func<Task> onEntry) : IOutboxPublisherV2
    {
        public int Calls { get; private set; }
        public TimeSpan Elapsed { get; private set; }
        public bool EntryVerified { get; private set; }
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct)
        {
            Calls++; var started = Stopwatch.GetTimestamp();
            try { await onEntry(); EntryVerified = true; await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(OutboxTransportDecisionV2.Unknown); }
            finally { Elapsed = Stopwatch.GetElapsedTime(started); Cancelled.TrySetResult(); }
        }
    }
    private async Task WaitForAdvisoryWait(int pid)
    {
        await using var db = await fixture.OpenAsync();
        await using var cmd = db.Database.GetDbConnection().CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM pg_locks WHERE pid=@pid AND locktype='advisory' AND NOT granted)";
        var p = cmd.CreateParameter(); p.ParameterName = "pid"; p.Value = pid; cmd.Parameters.Add(p);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (Equals(true, await cmd.ExecuteScalarAsync())) return;
            await Task.Delay(10);
        }
        Assert.Fail("Expected real PostgreSQL advisory-lock waiter was not observed within 5 seconds.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Epoch_and_completion_serialize_in_both_lock_orderings(bool advanceFirst) => Scenario(1, async (owner, api, ids) =>
    {
        if (advanceFirst) owner.LeaseDuration = TimeSpan.FromSeconds(1);
        var claim = await Claim(owner, api); var root = await Root(owner); var switchId = Guid.NewGuid();
        await Write(owner, api, tx => api.BeginDrainV2(tx, switchId, 1, OutboxEncodingV2.Encode(root.Version)));
        var draining = await Root(owner); var version = OutboxEncodingV2.Encode(draining.Version); var advanceId = Guid.NewGuid();
        var completion = new OutboxMutationV2(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1");
        if (advanceFirst)
        {
            await using var clock = await fixture.OpenAsync();
            await clock.Database.ExecuteSqlRawAsync("SELECT pg_sleep(1.1)");
        }
        await using var firstDb = await fixture.OpenAsync(); await using var secondDb = await fixture.OpenAsync();
        await using var firstNative = await firstDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var secondNative = await secondDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var first = await api.BindTransactionAsync(firstDb, firstNative.GetDbTransaction(), owner.Root);
        if (advanceFirst) Assert.Equal(2, (await api.AdvanceEpochV2(first, advanceId, switchId, 1, version)).Epoch);
        else Assert.Equal("Published", (await api.CompleteClaimV2(first, claim, completion)).Transition);
        var waiting = api.BindTransactionAsync(secondDb, secondNative.GetDbTransaction(), owner.Root);
        await WaitForAdvisoryWait(((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID);
        await firstNative.CommitAsync();
        var error = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            var second = await waiting;
            if (advanceFirst) await api.CompleteClaimV2(second, claim, completion);
            else await api.AdvanceEpochV2(second, advanceId, switchId, 1, version);
        });
        Assert.Equal("40001", error.SqlState); await secondNative.RollbackAsync();
        if (advanceFirst)
        {
            var before = await Snapshot(owner);
            Assert.Equal(OutboxDecisionV2.RouteEpochChanged, (await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, completion))).Decision);
            Assert.Equal(before, await Snapshot(owner));
            await using var check = await fixture.OpenAsync();
            Assert.True(await check.Set<OutboxAuditV2>().AnyAsync(x => x.RootId == owner.Root.Id && x.Kind == "UNCERTAIN_DELIVERY"));
        }
        else Assert.Equal(2, (await Write(owner, api, tx => api.AdvanceEpochV2(tx, advanceId, switchId, 1, version))).Epoch);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Hold_and_cleanup_use_same_root_in_both_lock_orderings(bool holdFirst) => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api);
        await Write(owner, api, tx => api.CompleteClaimV2(tx, claim, new(Guid.NewGuid(), claim.Ticket, OutboxIntentV2.PublishConfirmed, "fixture-policy-v1")));
        var candidate = await Candidate(owner, ids[0]);
        await using var firstDb = await fixture.OpenAsync(); await using var secondDb = await fixture.OpenAsync();
        await using var firstNative = await firstDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var secondNative = await secondDb.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var first = await api.BindTransactionAsync(firstDb, firstNative.GetDbTransaction(), owner.Root);
        if (holdFirst)
        {
            var authority = await firstDb.Set<Authority>().SingleAsync(x => x.RootId == owner.Root.Id);
            authority.Hold = true; await firstDb.SaveChangesAsync();
        }
        else Assert.Equal(ids, (await api.DeleteEligibleInTransactionV1(first, [candidate])).Deleted);
        var waiting = api.BindTransactionAsync(secondDb, secondNative.GetDbTransaction(), owner.Root);
        await WaitForAdvisoryWait(((NpgsqlConnection)secondDb.Database.GetDbConnection()).ProcessID);
        await firstNative.CommitAsync();
        if (holdFirst)
        {
            // The pre-lock Serializable snapshot cannot authorize deletion using stale hold evidence.
            // The native Owner row fence rejects that stale transaction during Bind itself.
            var conflict = await Record.ExceptionAsync(async () => { await waiting; });
            Assert.NotNull(conflict);
            while (conflict is not PostgresException && conflict.InnerException is not null) conflict = conflict.InnerException;
            Assert.Equal("40001", Assert.IsType<PostgresException>(conflict).SqlState);
            await secondNative.RollbackAsync();
            Assert.Equal(ids, (await Write(owner, api, tx => api.DeleteEligibleInTransactionV1(tx, [candidate]))).Retained);
        }
        else
        {
            await waiting;
            var authority = await secondDb.Set<Authority>().SingleAsync(x => x.RootId == owner.Root.Id);
            authority.Hold = true; await secondDb.SaveChangesAsync(); await secondNative.CommitAsync();
            await using var check = await fixture.OpenAsync();
            Assert.False(await check.Set<Cp6OutboxMessage>().AnyAsync(x => x.Id == ids[0]));
            Assert.True(await check.Set<OutboxAuditV2>().AnyAsync(x => x.RootId == owner.Root.Id && x.Kind == "RetentionDeleted"));
        }
    });
    [Fact]
    public Task Missing_guard_and_unsupported_provider_fail_closed() => Scenario(1, async (owner, api, ids) =>
    {
        var before = await Snapshot(owner);
        await fixture.WithNativeGuardDisabled(async () =>
        {
            var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1)));
            Assert.Equal("CP6_OUTBOX_GUARD_MIGRATION_REQUIRED", error.Code);
        });
        await using var db = await fixture.OpenAsync();
        await using var native = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        await using var unsupported = new DbContext(new DbContextOptionsBuilder().Options);
        var providerError = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => api.BindTransactionAsync(unsupported, native.GetDbTransaction(), owner.Root));
        Assert.Equal("CP6_OUTBOX_BINDING_INVALID", providerError.Code);
        Assert.Equal(before, await Snapshot(owner));
    });

    [Fact]
    public Task Missing_actual_owner_authority_row_fence_disables_write_binding() => Scenario(1, async (owner, api, ids) =>
    {
        var claim = await Claim(owner, api); var before = await Snapshot(owner);
        owner.RegisterAuthorityFence = false;
        var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1)));
        Assert.Equal("CP6_OUTBOX_AUTHORITY_FENCE_REQUIRED", error.Code);
        // Independent read recovery is intentionally not a write-authority operation.
        await using var db = await fixture.OpenAsync();
        var receipt = await db.Set<OutboxClaimReceiptV2>().SingleAsync(x => x.RootId == owner.Root.Id);
        Assert.Equal(OutboxDecisionV2.Committed, (await Read(owner, api, tx => api.QueryClaimV2(tx, receipt.RequestId, receipt.Input))).Decision);
        Assert.Equal(before, await Snapshot(owner));
    });

    [Theory]
    [InlineData("false-guard", "CP6_OUTBOX_GUARD_MIGRATION_REQUIRED")]
    [InlineData("missing-unique-index", "CP6_OUTBOX_INDEX_MIGRATION_REQUIRED")]
    public Task Actual_catalog_guard_predicate_and_unique_index_are_required(string mutation, string code) => Scenario(1, async (owner, api, ids) =>
    {
        var before = await Snapshot(owner);
        await fixture.WithCatalogMutation(mutation, async () =>
        {
            var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => Write(owner, api, tx => api.ClaimBatchV2(tx, Guid.NewGuid(), 1)));
            Assert.Equal(code, error.Code);
        });
        Assert.Equal(before, await Snapshot(owner));
        Assert.Equal(ids[0], (await Claim(owner, api)).Ticket.NativeId);
    });

    [Theory]
    [InlineData("binding-token")]
    [InlineData("root-column")]
    [InlineData("ignored-binding-token")]
    [InlineData("ignored-root-token")]
    public Task Altered_companion_token_or_root_mapping_is_rejected_before_dml(string mapping) => Scenario(1, async (owner, api, ids) =>
    {
        var before = await Snapshot(owner);
        await using TestDb db = mapping switch
        {
            "binding-token" => new WrongBindingTokenDb(fixture.RuntimeConnection),
            "ignored-binding-token" => new IgnoredBindingTokenDb(fixture.RuntimeConnection),
            "ignored-root-token" => new IgnoredRootTokenDb(fixture.RuntimeConnection),
            _ => new WrongRootColumnDb(fixture.RuntimeConnection)
        };
        await db.Database.OpenConnectionAsync();
        await fixture.VerifyAsync((NpgsqlConnection)db.Database.GetDbConnection());
        await using var native = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var error = await Assert.ThrowsAsync<OutboxProtocolExceptionV2>(() => api.BindTransactionAsync(db, native.GetDbTransaction(), owner.Root));
        Assert.Equal("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED", error.Code);
        Assert.Equal(before, await Snapshot(owner));
    });

    private sealed class WrongBindingTokenDb(string connection) : TestDb(connection)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model.Entity<OutboxLeaseBindingV2>().Property(x => x.Version).IsConcurrencyToken(false);
        }
    }
    private sealed class WrongRootColumnDb(string connection) : TestDb(connection)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model);
            model.Entity<OutboxRouteControlV2>().Property(x => x.RouteId).HasColumnName("WrongRouteId");
        }
    }
    private sealed class IgnoredBindingTokenDb(string connection) : TestDb(connection)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model); model.Entity<OutboxLeaseBindingV2>().Ignore(x => x.Version);
        }
    }
    private sealed class IgnoredRootTokenDb(string connection) : TestDb(connection)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            base.OnModelCreating(model); model.Entity<OutboxRouteControlV2>().Ignore(x => x.Version);
        }
    }
    private sealed class ProbePublisher(Func<Cp6OutboxDispatchMessage, Task<OutboxTransportResultV2>> action) : IOutboxPublisherV2
    {
        public int Calls { get; private set; }
        public async ValueTask<OutboxTransportResultV2> PublishAsync(Cp6OutboxDispatchMessage message, CancellationToken ct)
        {
            Calls++; return await action(message);
        }
    }
    private sealed class FutureClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow.AddYears(1);
    }
}
