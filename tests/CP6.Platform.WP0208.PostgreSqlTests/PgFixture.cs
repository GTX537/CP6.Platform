using System.Data;
using System.Text.Json;
using CP6.Platform.Abstractions.Context;
using CP6.Platform.Contracts.Context;
using CP6.Platform.EntityFramework.OutboxV2;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CP6.Platform.WP0208.PostgreSqlTests;

[CollectionDefinition("real-pg", DisableParallelization = true)]
public sealed class PgCollection : ICollectionFixture<PgFixture>;

public sealed class PgFixture : IAsyncLifetime
{
    private JsonElement _config;
    public string RuntimeConnection { get; private set; } = "";
    public string Owner { get; private set; } = "";
    private string Setting(string key) => _config.GetProperty(key).ToString();

    public async Task InitializeAsync()
    {
        var path = Environment.GetEnvironmentVariable("CP6_WP0208_SETTINGS") ??
            throw new InvalidOperationException("NOT_RUN: CP6_WP0208_SETTINGS must point to an explicitly owned isolated cluster.");
        var full = Path.GetFullPath(path);
        Assert.Equal("settings.json", Path.GetFileName(full));
        Assert.Equal(Path.GetFullPath("D:/CP6-local-test/WP02-08"), Directory.GetParent(full)!.Parent!.FullName);
        _config = JsonDocument.Parse(await File.ReadAllTextAsync(full)).RootElement.Clone();
        Owner = Setting("Owner");
        Assert.Equal(32, Owner.Length);
        Assert.Equal(Owner, Directory.GetParent(full)!.Name);
        Assert.Equal(55428, _config.GetProperty("Port").GetInt32());
        Assert.Equal(Path.GetFullPath(Setting("RunDirectory")), Directory.GetParent(full)!.FullName);
        Assert.Equal(Path.Combine(Directory.GetParent(full)!.FullName, "data"), Path.GetFullPath(Setting("DataDirectory")));
        Assert.Equal("CP6-WP02-08:" + Owner, Setting("Marker"));
        string Connection(bool admin) => new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 55428, Database = Setting("Database"),
            Username = Setting(admin ? "Admin" : "Runtime"), Password = Setting(admin ? "AdminPassword" : "RuntimePassword"),
            Pooling = false, Timeout = 5, CommandTimeout = 15, ApplicationName = "CP6-WP02-08:" + Owner,
            IncludeErrorDetail = false
        }.ConnectionString;
        RuntimeConnection = Connection(false);
        await using var admin = new NpgsqlConnection(Connection(true));
        await admin.OpenAsync();
        await VerifyAsync(admin, true);
        var sessions = (long)(await new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid()", admin).ExecuteScalarAsync())!;
        Assert.Equal(0L, sessions); // No session termination. Refuse reset if anyone else is using this exclusive cluster.
        await new NpgsqlCommand("CREATE OR REPLACE FUNCTION wp0208_guard.native_identity() RETURNS text LANGUAGE sql SECURITY DEFINER SET search_path=pg_catalog AS $$ SELECT system_identifier::text || '|' || current_setting('data_directory') FROM pg_control_system() $$; REVOKE ALL ON FUNCTION wp0208_guard.native_identity() FROM PUBLIC; " +
            $"GRANT EXECUTE ON FUNCTION wp0208_guard.native_identity() TO \"{Setting("Runtime")}\";", admin).ExecuteNonQueryAsync();
        await using (var model = new TestDb(Connection(true)))
        {
            // Guard table already exists, so EnsureCreated's any-table check is inappropriate.
            // Generate exactly this fixture model's DDL, after verifying cluster/database ownership.
            // Only this owned task fixture schema is reset; never a host schema/database. Logs/TRX retain prior runs.
            await new NpgsqlCommand("DROP SCHEMA fixture CASCADE; CREATE SCHEMA fixture;", admin).ExecuteNonQueryAsync();
            var script = model.Database.GenerateCreateScript();
            await new NpgsqlCommand(script, admin).ExecuteNonQueryAsync();
            await new NpgsqlCommand(OutboxPostgreSqlV2.GuardMigrationSql("fixture"), admin).ExecuteNonQueryAsync();
        }
        // Explicit DML rights only; no CREATE, superuser, bypass RLS or role management.
        await new NpgsqlCommand($"GRANT USAGE ON SCHEMA fixture TO \"{Setting("Runtime")}\"; GRANT SELECT,INSERT,UPDATE,DELETE ON ALL TABLES IN SCHEMA fixture TO \"{Setting("Runtime")}\"", admin).ExecuteNonQueryAsync();
        await using var runtime = new NpgsqlConnection(RuntimeConnection);
        await runtime.OpenAsync();
        await VerifyAsync(runtime, false);
    }

    public async Task VerifyAsync(NpgsqlConnection connection, bool admin = false)
    {
        await using var cmd = new NpgsqlCommand("SELECT inet_server_port(), current_database(), session_user, " +
            "(SELECT oid::bigint FROM pg_database WHERE datname=current_database()), " +
            "(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()), " +
            "(SELECT owner FROM wp0208_guard.resource), (SELECT marker FROM wp0208_guard.resource), " +
            "rolsuper,rolcreatedb,rolcreaterole,rolinherit,rolbypassrls FROM pg_roles WHERE rolname=session_user", connection);
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            Assert.True(await r.ReadAsync());
            Assert.Equal(55428, r.GetInt32(0)); Assert.Equal(Setting("Database"), r.GetString(1));
            Assert.Equal(Setting(admin ? "Admin" : "Runtime"), r.GetString(2));
            Assert.Equal(long.Parse(Setting("DatabaseOid"), System.Globalization.CultureInfo.InvariantCulture), r.GetInt64(3));
            Assert.Equal(Setting("Admin"), r.GetString(4)); Assert.Equal(Owner, r.GetString(5));
            Assert.Equal(Setting("Marker"), r.GetString(6));
            if (!admin) for (var i = 7; i <= 11; i++) Assert.False(r.GetBoolean(i));
        }
        if (admin)
        {
            var id = (string)(await new NpgsqlCommand("SELECT system_identifier::text FROM pg_control_system()", connection).ExecuteScalarAsync())!;
            Assert.Equal(Setting("SystemIdentifier"), id);
            var data = (string)(await new NpgsqlCommand("SHOW data_directory", connection).ExecuteScalarAsync())!;
            Assert.Equal(Path.GetFullPath(Setting("DataDirectory")), Path.GetFullPath(data));
        }
        else
        {
            var actual = ((string)(await new NpgsqlCommand("SELECT wp0208_guard.native_identity()", connection).ExecuteScalarAsync())!).Split('|');
            Assert.Equal(Setting("SystemIdentifier"), actual[0]);
            Assert.Equal(Path.GetFullPath(Setting("DataDirectory")), Path.GetFullPath(actual[1]));
        }
    }
    public async Task<TestDb> OpenAsync()
    {
        var db = new TestDb(RuntimeConnection);
        await db.Database.OpenConnectionAsync();
        await VerifyAsync((NpgsqlConnection)db.Database.GetDbConnection());
        return db;
    }
    public Task WithNativeGuardDisabled(Func<Task> action) => WithCatalogMutation("disabled-guard", action);
    public async Task WithCatalogMutation(string mutation, Func<Task> action)
    {
        var connection = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 55428, Database = Setting("Database"), Username = Setting("Admin"),
            Password = Setting("AdminPassword"), Pooling = false, Timeout = 5, CommandTimeout = 15,
            ApplicationName = "CP6-WP02-08:" + Owner, IncludeErrorDetail = false
        };
        await using var admin = new NpgsqlConnection(connection.ConnectionString);
        await admin.OpenAsync(); await VerifyAsync(admin, true);
        var sessions = (long)(await new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE backend_type='client backend' AND pid<>pg_backend_pid()", admin).ExecuteScalarAsync())!;
        Assert.Equal(0L, sessions);
        var (change, restore) = mutation switch
        {
            "disabled-guard" => ("ALTER TABLE fixture.\"Cp6_OutboxMessage\" DISABLE TRIGGER cp6_outbox_v2_guard", "ALTER TABLE fixture.\"Cp6_OutboxMessage\" ENABLE TRIGGER cp6_outbox_v2_guard"),
            "false-guard" => ("DROP TRIGGER cp6_outbox_v2_guard ON fixture.\"Cp6_OutboxMessage\"; CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON fixture.\"Cp6_OutboxMessage\" FOR EACH ROW WHEN (FALSE) EXECUTE FUNCTION fixture.cp6_outbox_v2_guard()",
                "DROP TRIGGER cp6_outbox_v2_guard ON fixture.\"Cp6_OutboxMessage\"; CREATE TRIGGER cp6_outbox_v2_guard BEFORE UPDATE OR DELETE ON fixture.\"Cp6_OutboxMessage\" FOR EACH ROW EXECUTE FUNCTION fixture.cp6_outbox_v2_guard()"),
            "missing-unique-index" => ("DROP INDEX fixture.\"IX_Cp6_OutboxBindingV2_MessageId\"", "CREATE UNIQUE INDEX \"IX_Cp6_OutboxBindingV2_MessageId\" ON fixture.\"Cp6_OutboxBindingV2\" (\"MessageId\")"),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        await new NpgsqlCommand(change, admin).ExecuteNonQueryAsync();
        try { await action(); }
        finally { await new NpgsqlCommand(restore, admin).ExecuteNonQueryAsync(); }
    }
    public Task DisposeAsync() { NpgsqlConnection.ClearAllPools(); return Task.CompletedTask; }
}

// Fixture authentication only. It cannot attest production sidecar authentication/registration.
public sealed class TestIdentity(Guid tenant) : IContextSourceVerifierV1<string>, IContextBindingAuthorityV1
{
    public ContextScopeAccessorV1 Accessor { get; } = new();
    private ContextActorV1 Actor => new(tenant, "https://fixture.invalid/issuer", "fixture-machine", ContextActorKindV1.Service, null);
    public ValueTask<ContextResolutionInputV1?> VerifyAsync(string input, CancellationToken cancellationToken = default)
    {
        Assert.Equal("authenticated-fixture", input);
        var now = DateTimeOffset.UtcNow;
        return ValueTask.FromResult<ContextResolutionInputV1?>(new(
            new(ContextEntryKindV1.Message, "fixture", "outbox-v2", "fixture", "receive", tenant, "native", "correlation", ["receive"]),
            new(Actor, Actor, "fixture", "region", now.AddSeconds(-1), now.AddMinutes(10), "fixture-auth", null, ["receive"])));
    }
    public ValueTask<ContextBindingRegistrationV1?> ResolveCurrentAsync(ContextOperationV1 operation,
        AuthenticatedContextSourceV1 source, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<ContextBindingRegistrationV1?>(new(tenant, tenant, "fixture", "outbox-v2", Actor.Issuer,
            "region", "fixture", "receive", ContextEntryKindV1.Message, Actor, Actor, "fixture-registration", "1",
            DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(10), true, ["receive"]));
    public Task Run(Func<Task> action) => new TrustedContextExecutorV1(this, Accessor)
        .ExecuteAsync(ContextEntryKindV1.Message, "authenticated-fixture", this, _ => action());
}
