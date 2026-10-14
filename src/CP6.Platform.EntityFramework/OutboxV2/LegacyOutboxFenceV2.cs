using System.Data;
using Microsoft.EntityFrameworkCore;

namespace CP6.Platform.EntityFramework.OutboxV2;

internal static class LegacyOutboxFenceV2
{
    // Detect the physical companion even when an older context lacks the opt-in model.
    // The database trigger is the final race/cutover fence; this predicate keeps ordinary unbound V1 work available.
    internal static async Task<IQueryable<T>> Unbound<T>(DbContext db, string identityColumn, CancellationToken ct) where T : class
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return db.Set<T>();
        var entity = db.Model.FindEntityType(typeof(T))!;
        var schema = entity.GetSchema() ?? "public";
        var binding = OutboxPostgreSqlV2.Table(schema, "Cp6_OutboxBindingV2");
        var connection = db.Database.GetDbConnection(); var opened = connection.State != ConnectionState.Open;
        try
        {
            if (opened) await db.Database.OpenConnectionAsync(ct);
            if (await OutboxPostgreSqlV2.Scalar(db, "SELECT to_regclass(@table)::text", ct, ("table", binding)) is not string) return db.Set<T>();
        }
        finally { if (opened) await db.Database.CloseConnectionAsync(); }
        var column = identityColumn == "Id" ? "NativeId" : "MessageId";
        var sql = $"SELECT n.*, n.xmin FROM {OutboxPostgreSqlV2.Table(schema, entity.GetTableName()!)} n WHERE NOT EXISTS (SELECT 1 FROM {binding} b WHERE b.{OutboxPostgreSqlV2.Quote(column)}=n.{OutboxPostgreSqlV2.Quote(identityColumn)})";
        return db.Set<T>().FromSqlRaw(sql);
    }
    internal static async Task RejectBound(DbContext db, Guid id, CancellationToken ct)
    {
        if (db.Database.ProviderName != "Npgsql.EntityFrameworkCore.PostgreSQL") return;
        var query = await Unbound<Cp6OutboxMessage>(db, "Id", ct);
        if (await db.Set<Cp6OutboxMessage>().AnyAsync(x => x.Id == id, ct) && !await query.AnyAsync(x => x.Id == id, ct))
            throw new OutboxProtocolExceptionV2("CP6_OUTBOX_V2_LEGACY_BYPASS");
    }
}
