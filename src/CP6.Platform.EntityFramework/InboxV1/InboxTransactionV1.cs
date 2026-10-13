using System.Data;
using System.Data.Common;
using CP6.Platform.Abstractions.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Platform.EntityFramework.InboxV1;

/// <summary>Live native caller transaction, not serialized proof. Does not own, begin, commit or dispose it.</summary>
public sealed class InboxTransactionV1
{
    private readonly ITrustedContextAccessorV1 _accessor;
    private readonly TrustedRequestContextV1 _trusted;
    private readonly string _identity;
    private readonly List<long> _locks = [];
    private InboxTransactionV1(DbContext db, DbTransaction transaction, ITrustedContextAccessorV1 accessor,
        TrustedRequestContextV1 trusted, string identity)
        => (Context, Transaction, _accessor, _trusted, _identity) = (db, transaction, accessor, trusted, identity);
    public DbContext Context { get; }
    public DbTransaction Transaction { get; }
    public DbConnection Connection => Context.Database.GetDbConnection();
    public Guid TenantId => _trusted.Context.TenantId;
    /// <summary>Diagnostic only; admission still checks real handles and native ownership.</summary>
    public string NativeIdentity => _identity;

    internal static async Task<InboxTransactionV1> BindAsync(DbContext db, DbTransaction transaction,
        ITrustedContextAccessorV1 accessor, bool readOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(transaction);
        var trusted = accessor.CurrentTrusted ?? throw new InboxProtocolExceptionV1("CP6_INBOX_TRUSTED_CONTEXT_REQUIRED");
        if (trusted.Context.TenantId == Guid.Empty || System.Transactions.Transaction.Current is not null ||
            db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL")
            throw new InboxProtocolExceptionV1("CP6_INBOX_NATIVE_TRANSACTION_REQUIRED");
        CheckHandles(db, transaction);
        var expectedIsolation = readOnly ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable;
        if (transaction.IsolationLevel != expectedIsolation)
            throw new InboxProtocolExceptionV1("CP6_INBOX_ISOLATION_INVALID");
        var mode = await Scalar(db, transaction, "SELECT current_setting('transaction_read_only')", [], ct);
        if ((string?)mode != (readOnly ? "on" : "off")) throw new InboxProtocolExceptionV1("CP6_INBOX_TRANSACTION_MODE_INVALID");
        var identity = (string)(await Scalar(db, transaction, IdentitySql, [], ct))!;
        return new(db, transaction, accessor, trusted, identity);
    }

    private const string IdentitySql = "SELECT current_database() || ':' || session_user || ':' || pg_backend_pid()::text || ':' || pg_current_xact_id()::text";
    private static void CheckHandles(DbContext db, DbTransaction transaction)
    {
        if (transaction.Connection is null || !ReferenceEquals(transaction.Connection, db.Database.GetDbConnection()) ||
            db.Database.CurrentTransaction is null || !ReferenceEquals(db.Database.CurrentTransaction.GetDbTransaction(), transaction) ||
            transaction.Connection.State != ConnectionState.Open)
            throw new InboxProtocolExceptionV1("CP6_INBOX_TRANSACTION_BINDING_INVALID");
    }

    internal async Task CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        CheckHandles(Context, Transaction);
        if (!ReferenceEquals(_accessor.CurrentTrusted, _trusted) ||
            (string?)(await Scalar(Context, Transaction, IdentitySql, [], ct)) != _identity)
            throw new InboxProtocolExceptionV1("CP6_INBOX_TRANSACTION_CHANGED");
        foreach (var key in _locks)
        {
            var held = await Scalar(Context, Transaction,
                "SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid() AND database=(SELECT oid FROM pg_database WHERE datname=current_database()) AND classid::bigint=@hi AND objid::bigint=@lo AND objsubid=1 AND mode='ExclusiveLock' AND granted)",
                [("hi", (long)(uint)((ulong)key >> 32)), ("lo", (long)(uint)key)], ct);
            if (held is not true) throw new InboxProtocolExceptionV1("CP6_INBOX_LOCK_NOT_HELD");
        }
    }

    internal async Task AcquireAsync(InboxLockPlanV1 plan, CancellationToken ct)
    {
        InboxValidationV1.Text(plan.Profile);
        InboxValidationV1.Text(plan.WriterSetReference);
        var locks = plan.Locks.ToArray();
        if (locks.Length != 7 || !locks.Select(x => x.Stage).SequenceEqual(Enum.GetValues<InboxLockStageV1>()) ||
            locks.Select(x => x.Key).Distinct().Count() != 7)
            throw new InboxProtocolExceptionV1("CP6_INBOX_LOCK_ORDER_INVALID");
        if ((long)(await Scalar(Context, Transaction,
            "SELECT count(*) FROM pg_locks WHERE pid=pg_backend_pid() AND locktype='advisory'", [], ct))! != 0)
            throw new InboxProtocolExceptionV1("CP6_INBOX_PRIOR_LOCK_ORDER_UNPROVEN");
        foreach (var item in locks)
        {
            await CheckAsync(ct);
            await Scalar(Context, Transaction, "SELECT pg_advisory_xact_lock(@key)", [("key", item.Key)], ct);
            _locks.Add(item.Key);
        }
        await CheckAsync(ct);
    }

    private static async Task<object?> Scalar(DbContext db, DbTransaction transaction, string sql,
        (string Name, object Value)[] parameters, CancellationToken ct)
    {
        CheckHandles(db, transaction);
        await using var command = transaction.Connection!.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var item in parameters)
        {
            var p = command.CreateParameter(); p.ParameterName = item.Name; p.Value = item.Value; command.Parameters.Add(p);
        }
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
}
