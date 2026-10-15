using System.Data;
using System.Diagnostics.Metrics;
using System.Text;
using CP6.Platform.Testing.EvidenceV1;
using Npgsql;
using Xunit.Abstractions;

namespace CP6.Platform.WP0226.PostgreSqlTests;

[Collection("owned-native-pg")]
public sealed class NativeEvidenceTests(PgFixture fixture, ITestOutputHelper output)
{
    private const string Credit = "999999999999999999999999999999.12345678";
    private const string Quantity = "1234567890123456789012345678901234567890.123456789012";
    private static readonly byte[] Raw = Encoding.UTF8.GetBytes(" {\"amount\":999999999999999999999999999999.12345678,\"quantity\":1234567890123456789012345678901234567890.123456789012} \n");

    private static async Task<int> Insert(NpgsqlConnection db, string caseId, string operation, string message)
    {
        await using var cmd = new NpgsqlCommand("INSERT INTO fixture.receipts VALUES (@case,@op,@msg,CAST(@amount AS numeric),CAST(@quantity AS numeric),@raw) ON CONFLICT(case_id,operation_key) DO NOTHING",db);
        cmd.Parameters.AddWithValue("case",caseId); cmd.Parameters.AddWithValue("op",operation); cmd.Parameters.AddWithValue("msg",message);
        cmd.Parameters.AddWithValue("amount",Credit); cmd.Parameters.AddWithValue("quantity",Quantity); cmd.Parameters.AddWithValue("raw",Raw);
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task AssertReceipt(NpgsqlConnection db,string caseId,string operation,string message)
    {
        await using var cmd = new NpgsqlCommand("SELECT operation_key,message_key,amount::text,quantity::text,raw_input FROM fixture.receipts WHERE case_id=@case",db);
        cmd.Parameters.AddWithValue("case",caseId);
        await using var row = await cmd.ExecuteReaderAsync();
        Assert.True(await row.ReadAsync()); Assert.Equal(operation,row.GetString(0)); Assert.Equal(message,row.GetString(1));
        ExactAssert.Equal(Credit,row.GetString(2)); ExactAssert.Equal(Quantity,row.GetString(3)); ExactAssert.BytesEqual(Raw,row.GetFieldValue<byte[]>(4));
        Assert.False(await row.ReadAsync());
    }

    [Fact]
    public async Task ConcurrentOriginalKeysCommitOnceAndPreserveNativeExactValues()
    {
        await fixture.ControlAsync("verify");
        var caseId=Guid.NewGuid().ToString("N"); var operation="operation:"+caseId; var message="message:"+caseId;
        await using var first=await fixture.OpenAsync(); await using var second=await fixture.OpenAsync();
        await using var tx1=await first.BeginTransactionAsync(); await using var tx2=await second.BeginTransactionAsync();
        Assert.Equal(1,await Insert(first,caseId,operation,message));
        var waiting=Insert(second,caseId,operation,message);
        // Prove the other native session is waiting on the unique-index transaction lock.
        var observed=false;
        for (var i=0;i<100;i++)
        {
            await using var check=new NpgsqlCommand("SELECT wait_event_type='Lock' FROM pg_stat_activity WHERE pid=@pid",first);
            check.Parameters.AddWithValue("pid",second.ProcessID);
            if (await check.ExecuteScalarAsync() is true) { observed=true; break; }
            await Task.Delay(20);
        }
        Assert.True(observed,"Second native transaction must actually block before first commit.");
        await tx1.CommitAsync(); Assert.Equal(0,await waiting); await tx2.CommitAsync();
        await AssertReceipt(second,caseId,operation,message);
        output.WriteLine($"Native PG concurrent duplicate: operation={operation}; message={message}; 1 insert + 0 duplicate; exact NUMERIC and raw bytes retained. Fixture Owner only.");
    }

    [Fact]
    public async Task NativeSerializationFailureIsDistinctFromInjectedAcknowledgmentSuppression()
    {
        await fixture.ControlAsync("verify");
        var id=Guid.NewGuid().ToString("N");
        await using var a=await fixture.OpenAsync(); await using var b=await fixture.OpenAsync();
        await using(var seed=new NpgsqlCommand("INSERT INTO fixture.balances VALUES (@id,9007199254740993)",a)) { seed.Parameters.AddWithValue("id",id); await seed.ExecuteNonQueryAsync(); }
        await using var ta=await a.BeginTransactionAsync(IsolationLevel.RepeatableRead); await using var tb=await b.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        async Task<string> Read(NpgsqlConnection db)
        {
            await using var cmd=new NpgsqlCommand("SELECT amount::text FROM fixture.balances WHERE case_id=@id",db); cmd.Parameters.AddWithValue("id",id); return (string)(await cmd.ExecuteScalarAsync())!;
        }
        ExactAssert.Equal("9007199254740993",await Read(a)); ExactAssert.Equal("9007199254740993",await Read(b));
        async Task Update(NpgsqlConnection db)
        {
            await using var cmd=new NpgsqlCommand("UPDATE fixture.balances SET amount=amount+1 WHERE case_id=@id",db); cmd.Parameters.AddWithValue("id",id); await cmd.ExecuteNonQueryAsync();
        }
        await Update(a); await ta.CommitAsync();
        var native=await Assert.ThrowsAsync<PostgresException>(()=>Update(b)); Assert.Equal("40001",native.SqlState); await tb.RollbackAsync();
        ExactAssert.Equal("9007199254740994",await Read(b));
        output.WriteLine("Observed native PostgreSQL SQLSTATE=40001 from overlapping RepeatableRead transactions; no injected SQLSTATE.");
    }

    [Fact]
    public async Task TechnicalSuccessBeforeRollbackDoesNotProveOwnerCommit()
    {
        await fixture.ControlAsync("verify");
        var id=Guid.NewGuid().ToString("N"); var count=0;
        using var meter=new Meter("wp0226."+id);
        using var listener=new MeterListener();
        listener.InstrumentPublished=(instrument,l)=> { if(instrument.Meter==meter) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<int>((_,v,_,_)=>Interlocked.Add(ref count,v)); listener.Start();
        var counter=meter.CreateCounter<int>("fixture.technical.success");
        await using var db=await fixture.OpenAsync();
        await using(var tx=await db.BeginTransactionAsync()) { await Insert(db,id,"operation:"+id,"message:"+id); counter.Add(1); await tx.RollbackAsync(); }
        Assert.Equal(1,count);
        await using var check=new NpgsqlCommand("SELECT count(*) FROM fixture.receipts WHERE case_id=@id",db); check.Parameters.AddWithValue("id",id);
        Assert.Equal(0L,await check.ExecuteScalarAsync());
        output.WriteLine("Actual technical counter=1; actual PostgreSQL rollback leaves receipt count=0. No business commit inferred.");
    }

    [Fact]
    public async Task InjectedLostApplicationAckReadsOriginalCommittedReceiptWithoutNewKeys()
    {
        await fixture.ControlAsync("verify");
        var id=Guid.NewGuid().ToString("N"); var op="operation:"+id; var msg="message:"+id;
        await using var db=await fixture.OpenAsync();
        async Task CommitThenSuppressAck()
        {
            await using var tx=await db.BeginTransactionAsync();
            await Insert(db,id,op,msg); await tx.CommitAsync();
            // The real COMMIT completed. Inject a lost response between Owner and caller.
            throw new TimeoutException("InjectedApplicationAckSuppression");
        }
        var injected=await Assert.ThrowsAsync<TimeoutException>(CommitThenSuppressAck);
        Assert.Equal("InjectedApplicationAckSuppression",injected.Message);
        var disposition="Unknown_InjectedApplicationAckSuppression";
        await AssertReceipt(db,id,op,msg);
        Assert.Equal(0,await Insert(db,id,op,msg)); await AssertReceipt(db,id,op,msg);
        output.WriteLine($"{disposition}; no native SQLSTATE claim; actual receipt readback and duplicate keep {op}/{msg}.");
    }

    [Fact]
    public async Task RestrictedRuntimeCannotCreateTruncateOrRewriteIdentityGuard()
    {
        await fixture.ControlAsync("verify");
        await using var db=await fixture.OpenAsync();
        foreach(var sql in new[] { "CREATE TABLE fixture.forbidden(value int)", "TRUNCATE fixture.receipts",
            "UPDATE wp0226_guard.resource SET marker='forged'", "CREATE SCHEMA forbidden" })
        {
            var error=await Assert.ThrowsAsync<PostgresException>(async()=> { await using var command=new NpgsqlCommand(sql,db); await command.ExecuteNonQueryAsync(); });
            Assert.Equal("42501",error.SqlState);
        }
        await fixture.VerifyAsync(db);
        output.WriteLine("Native PostgreSQL runtime denies CREATE, TRUNCATE and guard UPDATE with SQLSTATE 42501; identity unchanged.");
    }

    [Fact]
    public async Task ControlledOwnedClusterCrashRecoversCommittedBytesAndRollsBackUncommittedKeys()
    {
        await fixture.ControlAsync("verify");
        var id=Guid.NewGuid().ToString("N"); var pending=Guid.NewGuid().ToString("N");
        var op="operation:"+id; var msg="message:"+id;
        await using(var committed=await fixture.OpenAsync()) { await Insert(committed,id,op,msg); }
        var interrupted=await fixture.OpenAsync("crash");
        var tx=await interrupted.BeginTransactionAsync();
        await Insert(interrupted,pending,"operation:"+pending,"message:"+pending);
        var crashed=false;
        try
        {
            var report=await fixture.ControlAsync("crash",interrupted.ProcessID); crashed=true;
            Assert.True(report.GetProperty("ControlledFailure").GetBoolean());
            var error=await Record.ExceptionAsync(async()=> { await using var cmd=new NpgsqlCommand("SELECT 1",interrupted); await cmd.ExecuteScalarAsync(); });
            Assert.NotNull(error);
            output.WriteLine("Controlled own-cluster immediate shutdown; observed interrupted connection type="+error.GetType().Name+
                (error is PostgresException pg ? "; native SQLSTATE="+pg.SqlState : "; no SQLSTATE fabricated"));
        }
        finally
        {
            await tx.DisposeAsync(); await interrupted.DisposeAsync();
            if(crashed) await fixture.ControlAsync("start");
        }
        await using var recovered=await fixture.OpenAsync();
        await AssertReceipt(recovered,id,op,msg);
        await using var check=new NpgsqlCommand("SELECT count(*) FROM fixture.receipts WHERE case_id=@id",recovered); check.Parameters.AddWithValue("id",pending);
        Assert.Equal(0L,await check.ExecuteScalarAsync());
        Assert.Equal(0,await Insert(recovered,id,op,msg));
        output.WriteLine($"Same cluster {fixture.SystemIdentifier}; committed original keys/NUMERIC/raw bytes survive; uncommitted receipt absent; no production crash/adoption claim.");
    }
}
