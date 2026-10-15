using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Npgsql;

namespace CP6.Platform.WP0226.PostgreSqlTests;

[CollectionDefinition("owned-native-pg", DisableParallelization = true)]
public sealed class PgCollection : ICollectionFixture<PgFixture>;

// All test data is synthetic. This fixture is not an application Owner adapter.
public sealed class PgFixture : IAsyncLifetime
{
    private const string Python = "C:/Users/tt/AppData/Roaming/uv/python/cpython-3.13-windows-x86_64-none/python.exe";
    private JsonElement _settings;
    private string _settingsPath = "";
    private string _helper = "";
    public string Marker => Setting("Marker");
    public string SystemIdentifier => Setting("SystemIdentifier");
    private string Setting(string key) => _settings.GetProperty(key).ToString();

    public async Task InitializeAsync()
    {
        _settingsPath = Environment.GetEnvironmentVariable("CP6_WP0226_SETTINGS") ??
            throw new InvalidOperationException("NOT_RUN: explicitly owned WP02-26 PostgreSQL settings required.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
        Assert.NotNull(root);
        _helper = Path.Combine(root.FullName, "tests/CP6.Platform.WP0226.PostgreSqlTests/pg_resource.py");
        // Python verifies canonical contained paths, marker, exact system/OID/roles and zero sessions
        // BEFORE this fixture constructs a connection or executes any SQL.
        await ControlAsync("verify");
        _settings = JsonDocument.Parse(await File.ReadAllBytesAsync(_settingsPath)).RootElement.Clone();
    }

    public async Task<JsonElement> ControlAsync(string action, params int[] expectedPids)
    {
        var start = new ProcessStartInfo(Python) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add(_helper); start.ArgumentList.Add(action); start.ArgumentList.Add(_settingsPath);
        foreach (var pid in expectedPids) { start.ArgumentList.Add("--expected-pid"); start.ArgumentList.Add(pid.ToString(CultureInfo.InvariantCulture)); }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("resource-control-not-started");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(55));
        // Do not echo private provider diagnostics into the public test report.
        await stderr;
        Assert.True(process.ExitCode == 0, "Owned resource identity/lifecycle gate failed; private logs retained.");
        return JsonDocument.Parse(await stdout).RootElement.Clone();
    }

    public async Task<NpgsqlConnection> OpenAsync(string purpose = "test")
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = "127.0.0.1", Port = 55429, Database = Setting("Database"),
            Username = Setting("Runtime"), Password = Setting("RuntimePassword"),
            Pooling = false, Timeout = 5, CommandTimeout = 15, IncludeErrorDetail = false,
            ApplicationName = Marker+":"+purpose
        };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        try
        {
            await connection.OpenAsync();
            await VerifyAsync(connection);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task VerifyAsync(NpgsqlConnection connection)
    {
        const string query = "SELECT inet_server_port(),current_database(),session_user,(SELECT oid::bigint FROM pg_database WHERE datname=current_database()),"+
            "(SELECT pg_get_userbyid(datdba) FROM pg_database WHERE datname=current_database()),g.owner,g.marker,wp0226_guard.native_identity(),"+
            "rolsuper,rolcreatedb,rolcreaterole,rolinherit,rolbypassrls,has_database_privilege(current_database(),'CREATE'),"+
            "has_schema_privilege('fixture','CREATE'),has_table_privilege('wp0226_guard.resource','UPDATE') "+
            "FROM wp0226_guard.resource g CROSS JOIN pg_roles WHERE rolname=session_user";
        await using var cmd = new NpgsqlCommand(query,connection);
        await using var row = await cmd.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync());
        Assert.Equal(55429,row.GetInt32(0)); Assert.Equal(Setting("Database"),row.GetString(1));
        Assert.Equal(Setting("Runtime"),row.GetString(2));
        Assert.Equal(long.Parse(Setting("DatabaseOid"),CultureInfo.InvariantCulture),row.GetInt64(3));
        Assert.Equal(Setting("Admin"),row.GetString(4)); Assert.Equal(Setting("Owner"),row.GetString(5));
        Assert.Equal(Marker,row.GetString(6));
        var native = row.GetString(7).Split('|');
        Assert.Equal(SystemIdentifier,native[0]); Assert.Equal(Path.GetFullPath(Setting("DataDirectory")),Path.GetFullPath(native[1]));
        for (var i=8;i<16;i++) Assert.False(row.GetBoolean(i));
        Assert.False(await row.ReadAsync());
    }

    public async Task DisposeAsync()
    {
        // No pool clearing, process termination or data reset. A failed cleanup fails the suite.
        if (!_settings.Equals(default(JsonElement))) await ControlAsync("verify");
    }
}
