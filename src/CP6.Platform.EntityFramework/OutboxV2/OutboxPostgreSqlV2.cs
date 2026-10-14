using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CP6.Platform.EntityFramework.OutboxV2;

public static class OutboxPostgreSqlV2
{
    internal static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    internal static string Table(string schema, string name) => Quote(schema) + "." + Quote(name);
    internal static async Task<object?> Scalar(DbContext db, string sql, CancellationToken ct, params (string, object?)[] parameters)
    {
        await using var command = Command(db, sql, parameters);
        return await command.ExecuteScalarAsync(ct).ConfigureAwait(false);
    }
    internal static async Task<int> Execute(DbContext db, string sql, CancellationToken ct, params (string, object?)[] parameters)
    {
        await using var command = Command(db, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    internal static DbCommand Command(DbContext db, string sql, params (string, object?)[] parameters)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction(); command.CommandText = sql;
        foreach (var (name, value) in parameters) { var p = command.CreateParameter(); p.ParameterName = name; p.Value = value ?? DBNull.Value; command.Parameters.Add(p); }
        return command;
    }
    internal static async Task LockNative(OutboxTransactionV2 tx, IEnumerable<Guid> ids, CancellationToken ct)
    {
        foreach (var id in ids.Order())
            await Scalar(tx.Context, $"SELECT \"Id\" FROM {tx.Table("Cp6_OutboxMessage")} WHERE \"Id\"=@id FOR UPDATE", ct, ("id", id));
        foreach (var id in ids.Order())
            await Scalar(tx.Context, $"SELECT \"NativeId\" FROM {tx.Table("Cp6_OutboxBindingV2")} WHERE \"NativeId\"=@id FOR UPDATE", ct, ("id", id));
    }

    // These are migration assets, not runtime auto-DDL. A separately authorized Owner migration installs them.
    private static string GuardBody(string schema) => $$"""
        DECLARE nid uuid; rid text; lk bigint;
        BEGIN
          IF TG_TABLE_NAME = 'Cp6_OutboxMessage' THEN nid := OLD."Id";
          ELSIF TG_TABLE_NAME = 'Cp6_OutboxBindingV2' THEN nid := OLD."NativeId";
          ELSE
            SELECT "NativeId" INTO nid FROM {{Table(schema, "Cp6_OutboxBindingV2")}} WHERE "MessageId"=OLD."MessageId";
          END IF;
          SELECT b."RootId", r."LockKey" INTO rid, lk FROM {{Table(schema, "Cp6_OutboxBindingV2")}} b
            JOIN {{Table(schema, "Cp6_OutboxRouteV2")}} r ON r."Id"=b."RootId" WHERE b."NativeId"=nid;
          IF rid IS NOT NULL THEN
            IF current_setting('cp6.outbox_v2_writer', true) IS DISTINCT FROM pg_current_xact_id()::text || ':' || rid
              OR NOT EXISTS (SELECT 1 FROM pg_locks WHERE locktype='advisory' AND pid=pg_backend_pid()
                AND database=(SELECT oid FROM pg_database WHERE datname=current_database())
                AND classid::bigint=((lk >> 32) & 4294967295) AND objid::bigint=(lk & 4294967295)
                AND objsubid=1 AND mode='ExclusiveLock' AND granted) THEN
              RAISE EXCEPTION 'CP6_OUTBOX_V2_LEGACY_BYPASS' USING ERRCODE='55000';
            END IF;
            IF EXISTS (SELECT 1 FROM {{Table(schema, "Cp6_OutboxTokenSealV2")}} WHERE "NativeId"=nid AND "WriterTransaction"=pg_current_xact_id()::text) THEN
              RAISE EXCEPTION 'CP6_OUTBOX_TOKEN_ALREADY_RECORDED' USING ERRCODE='55000';
            END IF;
          END IF;
          IF TG_OP='DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
        END
        """;
    private const string ImmutableBody = "BEGIN RAISE EXCEPTION 'CP6_OUTBOX_IMMUTABLE_EVIDENCE' USING ERRCODE='55000'; END";
    public static string GuardMigrationSql(string schema)
    {
        OutboxEncodingV2.Text(schema);
        var sql = $"CREATE FUNCTION {Table(schema, "cp6_outbox_v2_guard")}() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $guard${GuardBody(schema)}$guard$;\n";
        foreach (var table in new[] { "Cp6_OutboxMessage", "Cp6_OutboxBindingV2", "Cp6_DeadLetterRecord" })
            sql += $"CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON {Table(schema, table)} FOR EACH ROW EXECUTE FUNCTION {Table(schema, "cp6_outbox_v2_guard")}();\n";
        sql += $"CREATE FUNCTION {Table(schema, "cp6_outbox_v2_immutable")}() RETURNS trigger LANGUAGE plpgsql SET search_path=pg_catalog AS $guard${ImmutableBody}$guard$;\n";
        foreach (var table in new[] { "Cp6_OutboxClaimReceiptV2", "Cp6_OutboxClaimTicketV2", "Cp6_OutboxMutationReceiptV2", "Cp6_OutboxAuditV2", "Cp6_OutboxTokenSealV2", "Cp6_OutboxSendAttemptV2" })
            sql += $"CREATE TRIGGER cp6_outbox_v2_immutable BEFORE UPDATE OR DELETE ON {Table(schema, table)} FOR EACH ROW EXECUTE FUNCTION {Table(schema, "cp6_outbox_v2_immutable")}();\n";
        return sql;
    }
    internal static async Task ValidateGuards(OutboxTransactionV2 tx, CancellationToken ct)
    {
        foreach (var (name, body, tables) in new[]
        {
            ("cp6_outbox_v2_guard", GuardBody(tx.Registration.Schema), new[] { "Cp6_OutboxMessage", "Cp6_OutboxBindingV2", "Cp6_DeadLetterRecord" }),
            ("cp6_outbox_v2_immutable", ImmutableBody, new[] { "Cp6_OutboxClaimReceiptV2", "Cp6_OutboxClaimTicketV2", "Cp6_OutboxMutationReceiptV2", "Cp6_OutboxAuditV2", "Cp6_OutboxTokenSealV2", "Cp6_OutboxSendAttemptV2" })
        })
        {
            foreach (var table in tables)
            {
                var valid = await Scalar(tx.Context,
                    "SELECT EXISTS(SELECT 1 FROM pg_trigger t JOIN pg_proc p ON p.oid=t.tgfoid JOIN pg_namespace n ON n.oid=p.pronamespace " +
                    "WHERE t.tgrelid=to_regclass(@table) AND t.tgname=@name AND t.tgenabled='O' AND t.tgtype=27 AND t.tgqual IS NULL AND t.tgattr=''::int2vector " +
                    "AND p.proname=@name AND n.nspname=@schema AND p.prosrc=@body AND NOT p.prosecdef AND p.proconfig=ARRAY['search_path=pg_catalog'] " +
                    "AND p.provolatile='v' AND p.prolang=(SELECT oid FROM pg_language WHERE lanname='plpgsql'))", ct,
                    ("table", tx.Table(table)), ("name", name), ("schema", tx.Registration.Schema), ("body", body));
                if (valid is not true) throw new OutboxProtocolExceptionV2("CP6_OUTBOX_GUARD_MIGRATION_REQUIRED");
            }
        }
        foreach (var (table, columns) in new[]
        {
            ("Cp6_OutboxMessage", new[] { "Id" }), ("Cp6_OutboxMessage", new[] { "MessageId" }),
            ("Cp6_OutboxRouteV2", new[] { "Id" }), ("Cp6_OutboxRouteV2", new[] { "OwnerId", "TenantId", "QueueDomainId", "RouteId" }),
            ("Cp6_OutboxRouteV2", new[] { "LockKey" }), ("Cp6_OutboxBindingV2", new[] { "NativeId" }),
            ("Cp6_OutboxBindingV2", new[] { "MessageId" }), ("Cp6_OutboxClaimReceiptV2", new[] { "RootId", "RequestId" }),
            ("Cp6_OutboxClaimTicketV2", new[] { "RootId", "RequestId", "NativeId" }), ("Cp6_OutboxClaimTicketV2", new[] { "ReceiptReference" }),
            ("Cp6_OutboxMutationReceiptV2", new[] { "RootId", "MutationId" }), ("Cp6_OutboxSendAttemptV2", new[] { "RootId", "ClaimId" }),
            ("Cp6_OutboxSendAttemptV2", new[] { "RootId", "MutationId" })
        })
        {
            if (await Scalar(tx.Context, "SELECT EXISTS(SELECT 1 FROM pg_index i WHERE i.indrelid=to_regclass(@table) " +
                "AND i.indisunique AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indexprs IS NULL " +
                "AND ARRAY(SELECT a.attname::text FROM unnest(i.indkey) WITH ORDINALITY k(num,ord) JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.num ORDER BY k.ord)=@columns)", ct,
                ("table", tx.Table(table)), ("columns", columns)) is not true)
                throw new OutboxProtocolExceptionV2("CP6_OUTBOX_INDEX_MIGRATION_REQUIRED");
        }
    }
}
