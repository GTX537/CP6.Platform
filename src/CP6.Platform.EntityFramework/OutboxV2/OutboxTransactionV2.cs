using System.Data;
using System.Data.Common;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Platform.EntityFramework.OutboxV2;

/// <summary>Live caller-owned transaction. Never begins, commits, rolls back or disposes the caller's unit of work.</summary>
public sealed class OutboxTransactionV2
{
    private readonly ITrustedContextAccessorV1 _accessor;
    private readonly TrustedRequestContextV1 _trusted;
    private readonly IOutboxOwnerV2 _owner;
    private readonly List<long> _locks = [];
    private readonly HashSet<Guid> _sealed = [];
    internal OutboxTransactionV2(DbContext db, DbTransaction transaction, ITrustedContextAccessorV1 accessor,
        TrustedRequestContextV1 trusted, IOutboxOwnerV2 owner, OutboxRegistrationV2 registration, bool readOnly, string identity, string xid)
        => (Context, Transaction, _accessor, _trusted, _owner, Registration, ReadOnly, NativeIdentity, TransactionId) =
            (db, transaction, accessor, trusted, owner, registration, readOnly, identity, xid);
    public DbContext Context { get; }
    public DbTransaction Transaction { get; }
    public OutboxRegistrationV2 Registration { get; }
    public bool ReadOnly { get; }
    public string NativeIdentity { get; }
    public string TransactionId { get; }
    public Guid TenantId => _trusted.Context.TenantId;
    internal string Table(string table) => OutboxPostgreSqlV2.Table(Registration.Schema, table);
    internal IOutboxOwnerV2 Owner => _owner;
    private const string IdentitySql = "SELECT current_database() || ':' || session_user || ':' || pg_backend_pid()::text || ':' || pg_current_xact_id()::text";

    internal static async Task<OutboxTransactionV2> Bind(DbContext db, DbTransaction native, ITrustedContextAccessorV1 accessor,
        IOutboxOwnerV2 owner, OutboxRootKeyV2 root, bool readOnly, CancellationToken ct)
    {
        var trusted = accessor.CurrentTrusted ?? throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TRUSTED_CONTEXT_REQUIRED");
        string? provider;
        try { provider = db.Database.ProviderName; }
        catch (InvalidOperationException) { throw new OutboxProtocolExceptionV2("CP6_OUTBOX_BINDING_INVALID"); }
        if (trusted.Context.TenantId != root.TenantId || root.TenantId == Guid.Empty || System.Transactions.Transaction.Current is not null ||
            provider != "Npgsql.EntityFrameworkCore.PostgreSQL") throw new OutboxProtocolExceptionV2("CP6_OUTBOX_BINDING_INVALID");
        Handles(db, native);
        if (native.IsolationLevel != (readOnly ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable))
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_ISOLATION_INVALID");
        if ((string?)await OutboxPostgreSqlV2.Scalar(db, "SHOW transaction_read_only", ct) != (readOnly ? "on" : "off"))
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TRANSACTION_MODE_INVALID");
        var registration = await owner.ResolveAsync(db, trusted, root, ct);
        if (registration.Root != root) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_REGISTRATION_MISMATCH");
        foreach (var text in new[] { root.OwnerId, root.QueueDomainId, root.RouteId, registration.Schema, registration.WorkerId,
            registration.PublisherSetReference, registration.MigrationReference, registration.WriterSetReference, registration.CutoverReference }) OutboxEncodingV2.Text(text);
        if (registration.MappingReference != OutboxEncodingV2.Mapping) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_MAPPING_REQUIRED");
        registration.Policy.Validate();
        OutboxModelV2.Validate(db.Model, registration.Schema);
        var identity = (string)(await OutboxPostgreSqlV2.Scalar(db, IdentitySql, ct))!;
        var xid = (string)(await OutboxPostgreSqlV2.Scalar(db, "SELECT pg_current_xact_id()::text", ct))!;
        var tx = new OutboxTransactionV2(db, native, accessor, trusted, owner,
            registration with { OwnerLockKeys = Array.AsReadOnly(registration.OwnerLockKeys.ToArray()),
                AuthorityRows = registration.AuthorityRows is null ? null : Array.AsReadOnly(registration.AuthorityRows.ToArray()) }, readOnly, identity, xid);
        if (!readOnly)
        {
            var keys = registration.OwnerLockKeys.Append(registration.RouteLockKey).ToArray();
            if (keys.Length is < 1 or > 17 || keys.Distinct().Count() != keys.Length ||
                (long)(await OutboxPostgreSqlV2.Scalar(db, "SELECT count(*) FROM pg_locks WHERE pid=pg_backend_pid() AND locktype='advisory'", ct))! != 0)
                throw new OutboxProtocolExceptionV2("CP6_OUTBOX_LOCK_ORDER_UNPROVEN");
            if (registration.AuthorityRows is null || registration.AuthorityRows.Count is < 1 or > 16)
                throw new OutboxProtocolExceptionV2("CP6_OUTBOX_AUTHORITY_FENCE_REQUIRED");
            foreach (var key in registration.OwnerLockKeys)
            {
                await OutboxPostgreSqlV2.Scalar(db, "SELECT pg_advisory_xact_lock(@key)", ct, ("key", key)); tx._locks.Add(key);
            }
            // Serializable snapshots can predate an advisory-lock wait. Actual row locks force 40001 if
            // any registered authority/hold/policy row changed since that snapshot; a mutex alone cannot.
            foreach (var fence in registration.AuthorityRows)
            {
                OutboxEncodingV2.Text(fence.Schema); OutboxEncodingV2.Text(fence.Table); OutboxEncodingV2.Text(fence.KeyColumn);
                if (fence.Key is not (string or Guid or long or int)) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_AUTHORITY_FENCE_INVALID");
                var held = await OutboxPostgreSqlV2.Scalar(db, $"SELECT 1 FROM {OutboxPostgreSqlV2.Table(fence.Schema, fence.Table)} WHERE {OutboxPostgreSqlV2.Quote(fence.KeyColumn)}=@key FOR UPDATE", ct, ("key", fence.Key));
                if (held is not 1) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_AUTHORITY_FENCE_MISSING");
            }
            await OutboxPostgreSqlV2.Scalar(db, "SELECT pg_advisory_xact_lock(@key)", ct, ("key", registration.RouteLockKey)); tx._locks.Add(registration.RouteLockKey);
            await OutboxPostgreSqlV2.Scalar(db, $"SELECT \"Id\" FROM {tx.Table("Cp6_OutboxRouteV2")} WHERE \"Id\"=@root FOR UPDATE", ct, ("root", root.Id));
            await OutboxPostgreSqlV2.ValidateGuards(tx, ct);
        }
        await tx.Check(readOnly ? OutboxAccessV2.ReadReceipt : OutboxAccessV2.Claim, ct, authorize: false);
        return tx;
    }
    private static void Handles(DbContext db, DbTransaction native)
    {
        if (native.Connection is null || native.Connection.State != ConnectionState.Open ||
            !ReferenceEquals(db.Database.GetDbConnection(), native.Connection) || db.Database.CurrentTransaction is null ||
            !ReferenceEquals(db.Database.CurrentTransaction.GetDbTransaction(), native))
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_NATIVE_TRANSACTION_REQUIRED");
    }
    internal async Task Check(OutboxAccessV2 access, CancellationToken ct, bool authorize = true)
    {
        Handles(Context, Transaction);
        if (!ReferenceEquals(_accessor.CurrentTrusted, _trusted) ||
            (string?)await OutboxPostgreSqlV2.Scalar(Context, IdentitySql, ct) != NativeIdentity ||
            (ReadOnly && access != OutboxAccessV2.ReadReceipt)) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TRANSACTION_CHANGED");
        foreach (var key in _locks)
        {
            if (await OutboxPostgreSqlV2.Scalar(Context,
                "SELECT EXISTS(SELECT 1 FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid() AND database=(SELECT oid FROM pg_database WHERE datname=current_database()) " +
                "AND classid::bigint=@hi AND objid::bigint=@lo AND objsubid=1 AND mode='ExclusiveLock' AND granted)", ct,
                ("hi", (long)(uint)((ulong)key >> 32)), ("lo", (long)(uint)key)) is not true)
                throw new OutboxProtocolExceptionV2("CP6_OUTBOX_ROOT_LOCK_LOST");
        }
        if (authorize) await _owner.AuthorizeAsync(this, access, ct);
    }
    internal async Task Permit(CancellationToken ct) => _ = await OutboxPostgreSqlV2.Scalar(Context,
        "SELECT set_config('cp6.outbox_v2_writer',@value,true)", ct, ("value", TransactionId + ":" + Registration.Root.Id));
    internal async Task Seal(Guid id, CancellationToken ct)
    {
        if (!_sealed.Add(id)) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TOKEN_ALREADY_RECORDED");
        Context.Add(new OutboxTokenSealV2 { NativeId = id, WriterTransaction = TransactionId });
        await Context.SaveChangesAsync(ct);
    }
    internal void EnsureUnsealed(Guid id) { if (_sealed.Contains(id)) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_TOKEN_ALREADY_RECORDED"); }
}
