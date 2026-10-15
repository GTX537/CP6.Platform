using System.Text.Json;
using CP6.Platform.EntityFramework.OutboxV2;
using CP6.Platform.Messaging.TransportV1;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Platform.WP0212.Fixture;

internal static class Check
{
    public static void That(bool value, string code) { if (!value) throw new InvalidOperationException(code); }
}

internal sealed class FixtureDatabase : IDbContextFactory<FixtureDb>, ITransportQuarantineStoreV1
{
    private readonly JsonElement config;
    public string Owner => Get("Owner");
    public string Directory => Get("RunDirectory");
    private string Get(string key) => config.GetProperty(key).ToString();
    public FixtureDatabase(string settings)
    {
        var full = Path.GetFullPath(settings);
        Check.That(Path.GetFileName(full) == "settings.json" &&
            Path.GetDirectoryName(Path.GetDirectoryName(full)) == Path.GetFullPath("D:/CP6-local-test/WP02-12"), "SETTINGS_PATH");
        config = JsonDocument.Parse(File.ReadAllBytes(full)).RootElement.Clone();
        Check.That(System.Text.RegularExpressions.Regex.IsMatch(Owner, "^[0-9a-f]{32}$") &&
            Path.GetFileName(Path.GetDirectoryName(full)) == Owner && Get("Marker") == "CP6-WP02-12:" + Owner &&
            Get("Port") == "55430" && Path.GetFullPath(Directory) == Path.GetDirectoryName(full), "SETTINGS_OWNER");
    }
    private string Connection(bool admin) => new NpgsqlConnectionStringBuilder
    {
        Host = "127.0.0.1", Port = 55430, Database = Get("Database"),
        Username = Get(admin ? "Admin" : "Runtime"), Password = Get(admin ? "AdminPassword" : "RuntimePassword"),
        Pooling = false, Timeout = 5, CommandTimeout = 15, IncludeErrorDetail = false,
        ApplicationName = "CP6-WP02-12:" + Owner
    }.ConnectionString;
    public async Task Verify(NpgsqlConnection connection, bool admin = false)
    {
        await using var cmd = new NpgsqlCommand("SELECT inet_server_port(),current_database(),session_user," +
            "(SELECT oid::bigint FROM pg_database WHERE datname=current_database())," +
            "(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database())," +
            "(SELECT owner FROM wp0212_guard.resource),(SELECT marker FROM wp0212_guard.resource)," +
            "rolsuper,rolcreatedb,rolcreaterole,rolinherit,rolbypassrls FROM pg_roles WHERE rolname=session_user", connection);
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            Check.That(await r.ReadAsync() && r.GetInt32(0)==55430 && r.GetString(1)==Get("Database") &&
                r.GetString(2)==Get(admin ? "Admin" : "Runtime") && r.GetInt64(3)==long.Parse(Get("DatabaseOid")) &&
                r.GetString(4)==Get("Admin") && r.GetString(5)==Owner && r.GetString(6)==Get("Marker"), "PG_NATIVE_IDENTITY");
            if (!admin) for(var i=7;i<=11;i++) Check.That(!r.GetBoolean(i), "RUNTIME_PRIVILEGE");
        }
        var sql = admin ? "SELECT system_identifier::text || '|' || current_setting('data_directory') FROM pg_control_system()"
                        : "SELECT wp0212_guard.native_identity()";
        var native = ((string)(await new NpgsqlCommand(sql,connection).ExecuteScalarAsync())!).Split('|');
        Check.That(native[0]==Get("SystemIdentifier") && Path.GetFullPath(native[1])==Path.GetFullPath(Get("DataDirectory")), "PG_CLUSTER_IDENTITY");
    }
    public async Task Initialize()
    {
        await using var admin=new NpgsqlConnection(Connection(true)); await admin.OpenAsync(); await Verify(admin,true);
        Check.That((long)(await new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid()",admin).ExecuteScalarAsync())! == 0, "UNEXPECTED_SESSION");
        await new NpgsqlCommand("CREATE FUNCTION wp0212_guard.native_identity() RETURNS text LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$ SELECT system_identifier::text || '|' || current_setting('data_directory') FROM pg_control_system() $$; REVOKE ALL ON FUNCTION wp0212_guard.native_identity() FROM PUBLIC; " +
            $"GRANT EXECUTE ON FUNCTION wp0212_guard.native_identity() TO \"{Get("Runtime")}\"",admin).ExecuteNonQueryAsync();
        await using var model=new FixtureDb(Connection(true));
        await new NpgsqlCommand(model.Database.GenerateCreateScript(),admin).ExecuteNonQueryAsync();
        await new NpgsqlCommand(OutboxPostgreSqlV2.GuardMigrationSql("fixture"),admin).ExecuteNonQueryAsync();
        await new NpgsqlCommand($"GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA fixture TO \"{Get("Runtime")}\"",admin).ExecuteNonQueryAsync();
    }
    public FixtureDb CreateDbContext() => CreateDbContextAsync().GetAwaiter().GetResult();
    public async Task<FixtureDb> CreateDbContextAsync(CancellationToken ct=default)
    {
        var db=new FixtureDb(Connection(false));
        try { await db.Database.OpenConnectionAsync(ct); await Verify((NpgsqlConnection)db.Database.GetDbConnection()); return db; }
        catch { await db.DisposeAsync(); throw; }
    }
    public async ValueTask PersistAsync(QuarantineRecordV1 original, CancellationToken ct)
    {
        await using var db=await CreateDbContextAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        // Conflict does not overwrite the original; independent readback below detects identity drift.
        var facts=JsonSerializer.Serialize(original.NativeFacts);
        var nativeReference=original.NativeFacts?.NativeReference ?? throw new InvalidOperationException("NATIVE_REFERENCE_REQUIRED");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO fixture.\"RawQuarantine\" (\"Profile\",\"Route\",\"Policy\",\"MessageId\",\"Hash\",\"Body\",\"FactsJson\",\"NativeReference\") VALUES ({original.Profile},{original.Route},{original.Policy},{original.MessageId},{original.RawEnvelopeSha256},{original.CopyRawBytes()},{facts},{nativeReference}) ON CONFLICT DO NOTHING",ct);
        await tx.CommitAsync(ct);
    }
    public async ValueTask<QuarantineRecordV1?> ReadCommittedAsync(string profile,string route,string policy,string messageId,string hash,string nativeReference,CancellationToken ct)
    {
        await using var db=await CreateDbContextAsync(ct);
        await using var tx=await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY",ct);
        var row=await db.Set<RawQuarantine>().AsNoTracking().SingleOrDefaultAsync(x=>x.Profile==profile && x.Route==route && x.Policy==policy && x.MessageId==messageId && x.Hash==hash && x.NativeReference==nativeReference,ct);
        return row is null ? null : new(row.Profile,row.Route,row.Policy,row.MessageId,row.Hash,row.Body,JsonSerializer.Deserialize<AuthenticatedTransportFactsV1>(row.FactsJson));
    }
}
internal sealed class FixtureDb(string connection) : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options) => options.UseNpgsql(connection);
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.AddPostgreSqlOutboxV2("fixture");
        m.Entity<AuthorityRow>().ToTable("OwnerAuthority","fixture").HasKey(x=>x.RootId);
        m.Entity<RawQuarantine>().ToTable("RawQuarantine","fixture").HasKey(x=>new{x.Profile,x.Route,x.Policy,x.MessageId,x.Hash,x.NativeReference});
        m.Entity<ReceiveObservation>().ToTable("ReceiveObservation","fixture").HasKey(x=>new{x.Topic,x.Partition,x.Offset});
    }
}
internal sealed class AuthorityRow
{
    public string RootId {get;set;}="";
    public long OwnerLock {get;set;}
    public long RouteLock {get;set;}
    public bool Write {get;set;}=true;
}
internal sealed class RawQuarantine
{
    public string Profile {get;set;}=""; public string Route {get;set;}=""; public string Policy {get;set;}="";
    public string MessageId {get;set;}=""; public string Hash {get;set;}=""; public byte[] Body {get;set;}=[];
    public string FactsJson {get;set;}="";
    public string NativeReference {get;set;}="";
}
internal sealed class ReceiveObservation
{
    public string Topic {get;set;}=""; public int Partition {get;set;} public long Offset {get;set;}
    public string Key {get;set;}=""; public string Hash {get;set;}=""; public byte[] Body {get;set;}=[];
    public string Qualification {get;set;}=""; public string Disposition {get;set;}="";
}
