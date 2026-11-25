using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IndustrialCopilot.IntegrationTests.Reasoning;

public class JobIsolationTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    [PostgresFact]
    public async Task JobsAreOwnerScopedAndActiveJobsAreBounded()
    {
        var equipment=Guid.NewGuid();var path=Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=Guid.NewGuid(),ManualRevisionId=Guid.NewGuid()},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=Array.Empty<object>(),ExplicitlyNoMandatoryRequirements=true}}));
            var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RAG_TEST_POSTGRES")){Database=new NpgsqlConnectionStringBuilder(database.Source.ConnectionString).Database}.ConnectionString;
            await using var app=ApiHost.Build(["--environment","Testing"],b=>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                    ["Safety:ProcedureFile"]=path,["ConnectionStrings:Operations"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,["Dispatch:Adapter"]="PostgresInbox",
                    ["Authentication:Credentials:0:Actor"]="operator",["Authentication:Credentials:0:Secret"]=new('a',32),["Authentication:Credentials:0:Permissions"]="read,start",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString(),
                    ["Authentication:Credentials:1:Actor"]="peer",["Authentication:Credentials:1:Secret"]=new('b',32),["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=equipment.ToString(),
                    ["Authentication:Credentials:2:Actor"]="boss",["Authentication:Credentials:2:Secret"]=new('c',32),["Authentication:Credentials:2:Permissions"]="read,start,approve",["Authentication:Credentials:2:EquipmentIds"]=equipment.ToString()});
                // No orchestrator/provider registered: a 202 here cannot hide request-owned execution.
                b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),false);
            });
            await MaintenanceHostRegistration.MigrateAsync(app.Services,false,default);await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),Timeout=TimeSpan.FromSeconds(10)};
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('a',32));
            client.DefaultRequestHeaders.Add("Accept-Language","ar-EG");client.DefaultRequestHeaders.Add("Idempotency-Key","http-test");

            HttpClient As(char key){var c=new HttpClient{BaseAddress=client.BaseAddress,Timeout=TimeSpan.FromSeconds(10)};c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string(key,32));return c;}
            async Task<HttpResponseMessage> Submit(HttpClient who,string key){using var m=new HttpRequestMessage(HttpMethod.Post,"/api/jobs"){Content=JsonContent.Create(new{equipmentId=equipment,symptom="vibration "+key})};m.Headers.Add("Idempotency-Key",key);return await who.SendAsync(m);}
            using var owner=As('a');using var peer=As('b');using var boss=As('c');
            var first=await Submit(owner,"own-1");Assert.Equal(HttpStatusCode.Accepted,first.StatusCode);
            var id=(await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid();
            // Same equipment, different actor: no read, stream or cancel.
            Assert.Equal(HttpStatusCode.Forbidden,(await peer.GetAsync($"/api/jobs/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await peer.GetAsync($"/api/jobs/{id}/events")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await peer.PostAsync($"/api/jobs/{id}/cancel",null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync($"/api/jobs/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await boss.GetAsync($"/api/jobs/{id}")).StatusCode);
            // Runs are owner-scoped too: the run id appears in the job view, but a peer cannot read the run.
            var runId=(await (await owner.GetAsync($"/api/jobs/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("maintenanceRunId").GetGuid();
            Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync($"/api/runs/{runId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await peer.GetAsync($"/api/runs/{runId}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK,(await boss.GetAsync($"/api/runs/{runId}")).StatusCode);
            // Per-actor cap: three active jobs are allowed, the fourth is refused, another actor is unaffected.
            Assert.Equal(HttpStatusCode.Accepted,(await Submit(owner,"own-2")).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted,(await Submit(owner,"own-3")).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests,(await Submit(owner,"own-4")).StatusCode);
            Assert.Equal(HttpStatusCode.Accepted,(await Submit(peer,"peer-1")).StatusCode);
            await app.StopAsync();
        }
        finally {File.Delete(path);}
    }

    [PostgresFact]
    public async Task ConcurrentSubmissionsFromManyActorsCannotExceedTheGlobalActiveCap()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var equipment=Guid.NewGuid();
        await using(var cmd=database.Source.CreateCommand("INSERT INTO operations.equipment VALUES(@id)")){cmd.Parameters.AddWithValue("id",equipment);await cmd.ExecuteNonQueryAsync();}
        var jobs=new PostgresReasoningJobStore(database.Source);
        async Task<bool> Submit(int n)
        {
            var r=new IndustrialCopilot.Application.Reasoning.MaintenanceReasoningRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("vibration "+n,[new(equipment,"pump",Guid.NewGuid(),Guid.NewGuid())],[]),"en-US");
            try{await jobs.SubmitAsync(new ReasoningJobSubmission("actor-"+n,"key-"+n,r),default);return true;}
            catch(JobCapacityException){return false;}
        }
        var results=await Task.WhenAll(Enumerable.Range(0,60).Select(i=>Task.Run(()=>Submit(i))));
        await using var count=database.Source.CreateCommand("SELECT count(*) FROM operations.reasoning_jobs WHERE status IN (1,2)");
        var active=(long)(await count.ExecuteScalarAsync())!;
        Assert.Equal(PostgresReasoningJobStore.MaxActiveJobsTotal,active);
        Assert.Equal(60-results.Count(x=>x),results.Count(x=>!x));
        Assert.Contains(false,results);
        // The cap is global to the database: free the slots so other tests sharing this fixture are unaffected.
        await using var release=database.Source.CreateCommand("DELETE FROM operations.reasoning_jobs WHERE status IN (1,2)");
        await release.ExecuteNonQueryAsync();
    }

    [PostgresFact]
    public async Task WorkOrderEvidenceDispatchAndTraceReadsAreOwnerScopedAgainstRealStore()
    {
        var equipment=Guid.NewGuid();var path=Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=Guid.NewGuid(),ManualRevisionId=Guid.NewGuid()},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=Array.Empty<object>(),ExplicitlyNoMandatoryRequirements=true}}));
            var connection=new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("RAG_TEST_POSTGRES")){Database=new NpgsqlConnectionStringBuilder(database.Source.ConnectionString).Database}.ConnectionString;
            await using var app=ApiHost.Build(["--environment","Testing"],b=>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                    ["Safety:ProcedureFile"]=path,["ConnectionStrings:Operations"]=connection,["ConnectionStrings:DispatchReceiver"]=connection,["Dispatch:Adapter"]="PostgresInbox",
                    ["Authentication:Credentials:0:Actor"]="operator",["Authentication:Credentials:0:Secret"]=new('a',32),["Authentication:Credentials:0:Permissions"]="read,start",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString(),
                    ["Authentication:Credentials:1:Actor"]="peer",["Authentication:Credentials:1:Secret"]=new('b',32),["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=equipment.ToString(),
                    ["Authentication:Credentials:2:Actor"]="boss",["Authentication:Credentials:2:Secret"]=new('c',32),["Authentication:Credentials:2:Permissions"]="read,start,approve",["Authentication:Credentials:2:EquipmentIds"]=equipment.ToString()});
                // No orchestrator/provider registered: a 202 here cannot hide request-owned execution.
                b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),false);
            });
            await MaintenanceHostRegistration.MigrateAsync(app.Services,false,default);await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),Timeout=TimeSpan.FromSeconds(10)};
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('a',32));
            client.DefaultRequestHeaders.Add("Accept-Language","ar-EG");client.DefaultRequestHeaders.Add("Idempotency-Key","http-test");

            HttpClient As(char key){var c=new HttpClient{BaseAddress=client.BaseAddress,Timeout=TimeSpan.FromSeconds(10)};c.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string(key,32));return c;}
            using var owner=As('a');using var peer=As('b');using var boss=As('c');
            // Seed a run, a work order and a trace owned by "operator" straight into the real stores.
            await using(var cmd=database.Source.CreateCommand("INSERT INTO operations.equipment VALUES(@id) ON CONFLICT DO NOTHING")){cmd.Parameters.AddWithValue("id",equipment);await cmd.ExecuteNonQueryAsync();}
            var store=new PostgresWorkflowStore(database.Source);
            var run=new IndustrialCopilot.Domain.MaintenanceRuns.MaintenanceRun(Guid.NewGuid(),equipment,"noise");run.Start();
            Assert.NotNull(await store.TrySaveRunAsync(run,null,default));
            var order=new IndustrialCopilot.Domain.WorkOrders.WorkOrder(Guid.NewGuid(),new(equipment,Guid.NewGuid(),Guid.NewGuid(),"noise","repair",[new(1,"isolate")]));
            Assert.NotNull(await store.TrySaveWorkOrderAsync(order,run.Id,null,default));
            var execution=Guid.NewGuid();
            Assert.True(await new PostgresRunTraceStore(database.Source,new IndustrialCopilot.IntegrationTests.Operations.TestAccessPolicy()).TrySaveAsync(
                new(execution,Guid.NewGuid(),run.Id,1,[new(Guid.NewGuid(),null,IndustrialCopilot.Application.Abstractions.Tracing.Models.TraceOperationKind.Llm,"completion",DateTimeOffset.Now,IndustrialCopilot.Application.Abstractions.Tracing.Models.TraceStepStatus.Running)]),0,default));
            await app.Services.GetRequiredService<IRunOwnership>().RecordAsync(run.Id,"operator",default);
            foreach(var url in new[]{$"/api/runs/{run.Id}",$"/api/work-orders/{order.Id}",$"/api/work-orders/{order.Id}/evidence",$"/api/traces/{execution}"})
            {
                Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync(url)).StatusCode);
                Assert.Equal(HttpStatusCode.Forbidden,(await peer.GetAsync(url)).StatusCode);
                Assert.Equal(HttpStatusCode.OK,(await boss.GetAsync(url)).StatusCode);
            }
            // Submit-for-review is a technician write on the same order: a peer must not touch it.
            Assert.Equal(HttpStatusCode.Forbidden,(await peer.PostAsJsonAsync($"/api/work-orders/{order.Id}/submit",new{revision=1,concurrencyToken="x"})).StatusCode);
            await app.StopAsync();
        }
        finally {File.Delete(path);}
    }
}
