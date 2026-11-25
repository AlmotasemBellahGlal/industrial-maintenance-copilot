using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IndustrialCopilot.IntegrationTests.Reasoning;

public class DemoSessionTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    private sealed record Host(WebApplication App,HttpClient Client,Guid Equipment,string ProcedurePath):IAsyncDisposable
    {
        public async ValueTask DisposeAsync(){Client.Dispose();await App.StopAsync();await App.DisposeAsync();File.Delete(ProcedurePath);}
    }
    private async Task<Host> Start(Dictionary<string,string?> extra,string environment="Testing")
    {
        var equipment=Guid.NewGuid();var path=Path.GetTempFileName();
        await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=Guid.NewGuid(),ManualRevisionId=Guid.NewGuid()},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=Array.Empty<object>(),ExplicitlyNoMandatoryRequirements=true}}));
        var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RAG_TEST_POSTGRES")){Database=new NpgsqlConnectionStringBuilder(database.Source.ConnectionString).Database}.ConnectionString;
        var settings=new Dictionary<string,string?>{
            ["Safety:ProcedureFile"]=path,["ConnectionStrings:Operations"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,["Dispatch:Adapter"]="PostgresInbox",
            ["Authentication:Credentials:0:Actor"]="operator",["Authentication:Credentials:0:Secret"]=new('a',32),["Authentication:Credentials:0:Permissions"]="read,start,approve",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString(),
            ["Demo:PublicSessions"]="true",["Demo:EquipmentId"]=equipment.ToString()};
        foreach(var (k,v) in extra)settings[k]=v;
        var app=ApiHost.Build(["--environment",environment],b=>
        {
            b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
            b.Configuration.AddInMemoryCollection(settings);
            b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),false);
        });
        await MaintenanceHostRegistration.MigrateAsync(app.Services,false,default);await app.StartAsync();
        var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),Timeout=TimeSpan.FromSeconds(10)};
        return new(app,client,equipment,path);
    }
    private static async Task<(HttpStatusCode Status,string? Token,JsonElement Body)> Mint(Host h,string? forwardedFor=null)
    {
        using var m=new HttpRequestMessage(HttpMethod.Post,"/api/demo/session");
        if(forwardedFor is not null)m.Headers.Add("X-Forwarded-For",forwardedFor);
        using var r=await h.Client.SendAsync(m);
        var body=await r.Content.ReadFromJsonAsync<JsonElement>();
        return (r.StatusCode,body.TryGetProperty("token",out var t)?t.GetString():null,body);
    }
    private static HttpClient As(Host h,string token){var c=new HttpClient{BaseAddress=h.Client.BaseAddress,Timeout=TimeSpan.FromSeconds(10)};c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",token);return c;}
    private static async Task<HttpResponseMessage> Submit(Host h,HttpClient who,string key){using var m=new HttpRequestMessage(HttpMethod.Post,"/api/jobs"){Content=JsonContent.Create(new{equipmentId=h.Equipment,symptom="vibration "+key})};m.Headers.Add("Idempotency-Key",key);return await who.SendAsync(m);}
    private async Task<long> Count(string sql,params (string,object)[] args)
    {
        await using var cmd=database.Source.CreateCommand(sql);foreach(var (n,v) in args)cmd.Parameters.AddWithValue(n,v);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    [PostgresFact]
    public async Task VisitorsGetIsolatedLeastPrivilegeSessionsAndConfiguredCredentialsStillWork()
    {
        await using var h=await Start(new());
        var a=await Mint(h);var b=await Mint(h);
        Assert.Equal(HttpStatusCode.Created,a.Status);Assert.Equal(HttpStatusCode.Created,b.Status);Assert.NotEqual(a.Token,b.Token);
        Assert.Equal(47,a.Token!.Length);
        using var visitorA=As(h,a.Token);using var visitorB=As(h,b.Token);using var operatorClient=As(h,new string('a',32));
        var submit=await Submit(h,visitorA,"v-a-1");Assert.Equal(HttpStatusCode.Accepted,submit.StatusCode);
        var job=(await submit.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        Assert.Equal(HttpStatusCode.OK,(await visitorA.GetAsync($"/api/jobs/{job}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await visitorB.GetAsync($"/api/jobs/{job}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await visitorB.PostAsync($"/api/jobs/{job}/cancel",null)).StatusCode);
        // Visitors never receive supervisor rights: the identity shows only read and start.
        var identity=await (await visitorA.GetAsync("/api/identity")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain("approve",identity.GetProperty("permissions").EnumerateArray().Select(x=>x.GetString()));
        Assert.Equal(HttpStatusCode.OK,(await operatorClient.GetAsync($"/api/jobs/{job}")).StatusCode);
        // Garbage, wrong-format and lookalike tokens are refused.
        using var bad=As(h,"dms_"+new string('A',43));Assert.Equal(HttpStatusCode.Unauthorized,(await bad.GetAsync($"/api/jobs/{job}")).StatusCode);
        using var shape=As(h,"short");Assert.Equal(HttpStatusCode.Unauthorized,(await shape.GetAsync($"/api/jobs/{job}")).StatusCode);
        // Visitor secrets are stored only as a hash.
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.demo_sessions WHERE encode(secret_hash,'escape') LIKE '%dms_%'"));
    }

    [PostgresFact]
    public async Task MintLimitsUseTheTrustedClientAddressOnlyAndDoNotSpendActorBudgets()
    {
        await using(var trusted=await Start(new(){["Demo:TrustedProxyCidr"]="127.0.0.0/8",["Demo:MaxSessionsPerClientPerHour"]="2",["Demo:MaxLiveSessions"]="1000",["Demo:MaxStoredRows"]="1000000"}))
        {
            await Reset();
            Assert.Equal(HttpStatusCode.Created,(await Mint(trusted,"127.0.0.10")).Status);
            Assert.Equal(HttpStatusCode.Created,(await Mint(trusted,"127.0.0.10")).Status);
            var limited=await Mint(trusted,"127.0.0.10");Assert.Equal(HttpStatusCode.TooManyRequests,limited.Status);
            Assert.Equal("session_limit",limited.Body.GetProperty("error").GetString());
            // A different real client behind the trusted proxy is unaffected.
            Assert.Equal(HttpStatusCode.Created,(await Mint(trusted,"127.0.0.11")).Status);
        }
        // Without a trusted proxy network, forwarded headers are ignored: spoofing cannot dodge the limit.
        await using(var untrusted=await Start(new(){["Demo:MaxSessionsPerClientPerHour"]="2",["Demo:MaxLiveSessions"]="1000",["Demo:MaxStoredRows"]="1000000"}))
        {
            await Reset();
            Assert.Equal(HttpStatusCode.Created,(await Mint(untrusted,"127.0.0.21")).Status);
            Assert.Equal(HttpStatusCode.Created,(await Mint(untrusted,"127.0.0.22")).Status);
            Assert.Equal(HttpStatusCode.TooManyRequests,(await Mint(untrusted,"127.0.0.23")).Status);
        }
    }

    [PostgresFact]
    public async Task MintBurstsHitTheirOwnLimiterAndLeaveActorBudgetsAlone()
    {
        await using var h=await Start(new(){["Security:MintPermitsPerClientPerMinute"]="3",["Demo:MaxSessionsPerClientPerHour"]="100",["Demo:MaxLiveSessions"]="1000",["Demo:MaxStoredRows"]="1000000"});
        await Reset();
        var codes=new List<HttpStatusCode>();for(var i=0;i<6;i++)codes.Add((await Mint(h)).Status);
        Assert.Equal(3,codes.Count(c=>c==HttpStatusCode.Created));Assert.Equal(3,codes.Count(c=>c==HttpStatusCode.TooManyRequests));
        // The operator's mutation budget is untouched by the flood.
        using var op=As(h,new string('a',32));
        Assert.Equal(HttpStatusCode.Accepted,(await Submit(h,op,"after-flood")).StatusCode);
    }

    [PostgresFact]
    public async Task ConcurrentMintsCannotOvershootTheLiveSessionCap()
    {
        await using var h=await Start(new());await Reset();
        var sessions=new PostgresDemoSessions(database.Source,new(MaxLive:5,MaxPerClientPerHour:1000,MaxStoredRows:1000000));
        var results=await Task.WhenAll(Enumerable.Range(0,40).Select(i=>Task.Run(()=>sessions.MintAsync("client-"+i,default))));
        Assert.Equal(5,results.Count(r=>r.Outcome==DemoMintOutcome.Created));
        Assert.Equal(35,results.Count(r=>r.Outcome==DemoMintOutcome.CapacityFull));
        Assert.Equal(5,await Count("SELECT count(*) FROM operations.demo_sessions"));
        // One client hammering in parallel is also bounded exactly.
        await Reset();
        var perClient=new PostgresDemoSessions(database.Source,new(MaxLive:1000,MaxPerClientPerHour:4,MaxStoredRows:1000000));
        var same=await Task.WhenAll(Enumerable.Range(0,30).Select(_=>Task.Run(()=>perClient.MintAsync("same-client",default))));
        Assert.Equal(4,same.Count(r=>r.Outcome==DemoMintOutcome.Created));
        // The stored-row guard refuses new sessions when the database already holds too much visitor data.
        await Reset();
        var (manual,revision)=await Seed(h.Equipment);
        for(var i=0;i<100;i++)
        {
            await using var c=database.Source.CreateCommand("INSERT INTO operations.conversations(id,actor,equipment_id,document_id,revision_id,culture) VALUES(@id,'filler',@e,@m,@r,'en-US')");
            c.Parameters.AddWithValue("id",Guid.NewGuid());c.Parameters.AddWithValue("e",h.Equipment);c.Parameters.AddWithValue("m",manual);c.Parameters.AddWithValue("r",revision);await c.ExecuteNonQueryAsync();
        }
        var full=new PostgresDemoSessions(database.Source,new(MaxLive:1000,MaxPerClientPerHour:1000,MaxStoredRows:100));
        Assert.Equal(DemoMintOutcome.CapacityFull,(await full.MintAsync("fresh",default)).Outcome);
        await Reset();
    }

    [PostgresFact]
    public async Task ExpiredSessionsStopAuthenticatingAndCleanupRemovesOnlyTheirOwnData()
    {
        await using var h=await Start(new());await Reset();
        var (manual,revision)=await Seed(h.Equipment);
        var a=await Mint(h);var b=await Mint(h);
        using var visitorA=As(h,a.Token!);using var visitorB=As(h,b.Token!);
        var jobA=(await (await Submit(h,visitorA,"a-1")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        var jobB=(await (await Submit(h,visitorB,"b-1")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
        var runA=(await (await visitorA.GetAsync($"/api/jobs/{jobA}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("maintenanceRunId").GetGuid();
        await AddArtifacts(runA,manual,revision,h.Equipment);
        var sessions=h.App.Services.GetRequiredService<IDemoSessions>();
        var seededEquipment=await Count("SELECT count(*) FROM operations.equipment");var seededManuals=await Count("SELECT count(*) FROM operations.manuals");
        // Not expired: nothing is removed.
        Assert.Equal(0,await sessions.PurgeExpiredAsync(10,default));
        await using(var expire=database.Source.CreateCommand("UPDATE operations.demo_sessions SET expires_at=clock_timestamp()-interval '1 minute' WHERE actor=(SELECT actor FROM operations.run_owners WHERE run_id=@run)")){expire.Parameters.AddWithValue("run",runA);Assert.Equal(1,await expire.ExecuteNonQueryAsync());}
        // Expired sessions stop authenticating immediately, before any cleanup runs.
        Assert.Equal(HttpStatusCode.Unauthorized,(await visitorA.GetAsync($"/api/jobs/{jobA}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await visitorB.GetAsync($"/api/jobs/{jobB}")).StatusCode);
        // A queued job keeps its owner's data until it settles.
        Assert.Equal(0,await sessions.PurgeExpiredAsync(10,default));
        Assert.Equal(1,await Count("SELECT count(*) FROM operations.reasoning_jobs WHERE id=@id",("id",jobA)));
        await using(var done=database.Source.CreateCommand("UPDATE operations.reasoning_jobs SET status=3 WHERE id=@id")){done.Parameters.AddWithValue("id",jobA);await done.ExecuteNonQueryAsync();}
        Assert.Equal(1,await sessions.PurgeExpiredAsync(10,default));
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.reasoning_jobs WHERE id=@id",("id",jobA)));
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.runs WHERE id=@id",("id",runA)));
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.run_owners WHERE run_id=@id",("id",runA)));
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.work_orders WHERE run_id=@id",("id",runA)));
        Assert.Equal(0,await Count("SELECT count(*) FROM operations.traces WHERE run_id=@id",("id",runA)));
        // The other visitor and the shared seeded demo data are untouched.
        Assert.Equal(1,await Count("SELECT count(*) FROM operations.reasoning_jobs WHERE id=@id",("id",jobB)));
        Assert.Equal(seededEquipment,await Count("SELECT count(*) FROM operations.equipment"));
        Assert.Equal(seededManuals,await Count("SELECT count(*) FROM operations.manuals"));
        Assert.Equal(HttpStatusCode.OK,(await visitorB.GetAsync($"/api/jobs/{jobB}")).StatusCode);
    }

    [PostgresFact]
    public async Task VisitorGrowthIsBoundedAtTheWriteSoExistingSessionsCannotBypassAfterMint()
    {
        await using var h=await Start(new());await Reset();
        var (manual,revision)=await Seed(h.Equipment);
        var store=new PostgresConversationStore(database.Source,new(MaxStoredRows:1000000));
        var actor="visitor-growth";
        // Per-session conversation bound, race-safe: 40 parallel creates produce exactly the limit.
        var created=await Task.WhenAll(Enumerable.Range(0,40).Select(_=>Task.Run(async()=>{try{await store.CreateAsync(actor,h.Equipment,manual,revision,"en-US",default);return true;}catch(DemoLimitException){return false;}})));
        Assert.Equal(20,created.Count(x=>x));
        Assert.Equal(20,await Count("SELECT count(*) FROM operations.conversations WHERE actor=@a",("a",actor)));
        // Another visitor is unaffected; a configured credential is never limited.
        await store.CreateAsync("visitor-other",h.Equipment,manual,revision,"en-US",default);
        for(var i=0;i<25;i++)await store.CreateAsync("operator-"+i,h.Equipment,manual,revision,"en-US",default);
        // Per-conversation turn bound.
        var list=await store.ListAsync(actor,[h.Equipment],0,50,default);
        var first=list[0].Id;
        for(var i=0;i<PostgresDemoSessionsLimits.TurnsPerConversation;i++)
        {
            var turn=await store.BeginAsync(actor,first,"q"+i,Guid.NewGuid(),default);Assert.NotNull(turn);
            Assert.True(await store.FinishAsync(actor,first,turn!.Value,IndustrialCopilot.Application.Ask.AskState.Completed,"a",[],default));
        }
        await Assert.ThrowsAsync<DemoLimitException>(()=>store.BeginAsync(actor,first,"one too many",Guid.NewGuid(),default));
        // Per-session turn bound across conversations: 30 used, 170 more allowed over other conversations.
        var used=PostgresDemoSessionsLimits.TurnsPerConversation;
        foreach(var conversation in list.Skip(1))
        {
            while(used<PostgresDemoSessionsLimits.TurnsPerSession)
            {
                Guid? turn;
                try{turn=await store.BeginAsync(actor,conversation.Id,"q",Guid.NewGuid(),default);}catch(DemoLimitException){break;}
                Assert.NotNull(turn);await store.FinishAsync(actor,conversation.Id,turn!.Value,IndustrialCopilot.Application.Ask.AskState.Completed,"a",[],default);used++;
            }
        }
        Assert.Equal(PostgresDemoSessionsLimits.TurnsPerSession,used);
        Assert.Equal(PostgresDemoSessionsLimits.TurnsPerSession,await Count("SELECT count(*) FROM operations.ask_turns k JOIN operations.conversations v ON v.id=k.conversation_id WHERE v.actor=@a",("a",actor)));
        await Assert.ThrowsAsync<DemoLimitException>(()=>store.BeginAsync(actor,list[1].Id,"over",Guid.NewGuid(),default));
        // Global storage guard at the write: a session minted long before still cannot add rows once storage is full.
        var rows=await Count("SELECT (SELECT count(*) FROM operations.reasoning_jobs)+(SELECT count(*) FROM operations.conversations)+(SELECT count(*) FROM operations.ask_turns)");
        var tight=new PostgresConversationStore(database.Source,new(MaxStoredRows:(int)rows));
        await Assert.ThrowsAsync<DemoLimitException>(()=>tight.CreateAsync("visitor-late",h.Equipment,manual,revision,"en-US",default));
        await tight.CreateAsync("operator-late",h.Equipment,manual,revision,"en-US",default);
        var jobs=new PostgresReasoningJobStore(database.Source,new(MaxStoredRows:(int)rows));
        IndustrialCopilot.Application.Reasoning.MaintenanceReasoningRequest Request()=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("vibration",[new(h.Equipment,"pump",manual,revision)],[]),"en-US");
        await Assert.ThrowsAsync<IndustrialCopilot.Application.Abstractions.Jobs.JobCapacityException>(()=>jobs.SubmitAsync(new("visitor-late","k1",Request()),default));
        await jobs.SubmitAsync(new("operator-late","k2",Request()),default);
        await using(var cleanup=database.Source.CreateCommand("DELETE FROM operations.ask_turns USING operations.conversations v WHERE v.id=ask_turns.conversation_id AND v.actor LIKE 'visitor-%' OR v.actor LIKE 'operator-%'; DELETE FROM operations.conversations WHERE actor LIKE 'visitor-%' OR actor LIKE 'operator-%'; DELETE FROM operations.reasoning_jobs WHERE actor='operator-late'"))
            await cleanup.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task ProductionModeHidesOpenApi()
    {
        await using var h=await Start(new(),"Production");
        Assert.Equal(HttpStatusCode.NotFound,(await h.Client.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [PostgresFact]
    public async Task PublicSessionsAreOffByDefault()
    {
        await using var h=await Start(new(){["Demo:PublicSessions"]="false"});
        Assert.Equal(HttpStatusCode.NotFound,(await h.Client.PostAsync("/api/demo/session",null)).StatusCode);
    }

    private async Task Reset()
    {
        await using var cmd=database.Source.CreateCommand("DELETE FROM operations.conversations WHERE actor='filler'; DELETE FROM operations.demo_sessions");
        await cmd.ExecuteNonQueryAsync();
    }
    private async Task<(Guid Manual,Guid Revision)> Seed(Guid equipment)
    {
        var manual=Guid.NewGuid();var revision=Guid.NewGuid();
        await using var cmd=database.Source.CreateCommand("INSERT INTO operations.equipment(id) VALUES(@e) ON CONFLICT DO NOTHING; INSERT INTO operations.manuals(id,equipment_id) VALUES(@m,@e); INSERT INTO operations.manual_revisions(id,manual_id) VALUES(@r,@m)");
        cmd.Parameters.AddWithValue("e",equipment);cmd.Parameters.AddWithValue("m",manual);cmd.Parameters.AddWithValue("r",revision);
        await cmd.ExecuteNonQueryAsync();return (manual,revision);
    }
    // Rows the worker would normally produce for a finished run: a work order with its snapshot, action and trace.
    private async Task AddArtifacts(Guid run,Guid manual,Guid revision,Guid equipment)
    {
        var order=Guid.NewGuid();var token=Guid.NewGuid();
        await using var connection=await database.Source.OpenConnectionAsync();await using var tx=await connection.BeginTransactionAsync();
        async Task Do(string sql){await using var c=new NpgsqlCommand(sql,connection,tx);c.Parameters.AddWithValue("o",order);c.Parameters.AddWithValue("t",token);c.Parameters.AddWithValue("r",run);c.Parameters.AddWithValue("m",manual);c.Parameters.AddWithValue("v",revision);c.Parameters.AddWithValue("e",equipment);await c.ExecuteNonQueryAsync();}
        await Do("INSERT INTO operations.work_orders(id,token,run_id) VALUES(@o,@t,@r)");
        await Do("INSERT INTO operations.work_order_snapshots(id,token,revision,status,equipment_id,manual_id,manual_revision_id,symptom,description) VALUES(@o,@t,1,1,@e,@m,@v,'s','d')");
        await Do("INSERT INTO operations.actions(id,token,action_order,instruction) VALUES(@o,@t,1,'x')");
        await Do("INSERT INTO operations.traces(execution_id,correlation_id,run_id,version,payload) VALUES(gen_random_uuid(),gen_random_uuid(),@r,1,'{}')");
        await tx.CommitAsync();
    }
}

internal static class PostgresDemoSessionsLimits
{
    public const int TurnsPerConversation=30, TurnsPerSession=200;
}
