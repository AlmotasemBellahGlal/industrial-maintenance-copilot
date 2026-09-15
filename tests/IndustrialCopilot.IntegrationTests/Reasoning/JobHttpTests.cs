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

public class JobHttpTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    [PostgresFact]
    public async Task SubmissionIsDurableWithoutOrchestratorInspectionAndObserverDisconnectDoNotOwnExecution()
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
                    ["Authentication:Credentials:1:Actor"]="outsider",["Authentication:Credentials:1:Secret"]=new('b',32),["Authentication:Credentials:1:Permissions"]="read,start",["Authentication:Credentials:1:EquipmentIds"]=Guid.NewGuid().ToString()});
                // No orchestrator/provider registered: a 202 here cannot hide request-owned execution.
                b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),false);
            });
            await MaintenanceHostRegistration.MigrateAsync(app.Services,false,default);await app.StartAsync();
            using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single()),Timeout=TimeSpan.FromSeconds(10)};
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('a',32));
            client.DefaultRequestHeaders.Add("Accept-Language","ar-EG");client.DefaultRequestHeaders.Add("Idempotency-Key","http-test");
            var response=await client.PostAsJsonAsync("/api/jobs",new{equipmentId=equipment,symptom="vibration"});Assert.Equal(HttpStatusCode.Accepted,response.StatusCode);
            var view=await response.Content.ReadFromJsonAsync<JsonElement>();var id=view.GetProperty("jobId").GetGuid();Assert.Equal("Queued",view.GetProperty("status").GetString());
            var duplicate=await client.PostAsJsonAsync("/api/jobs",new{equipmentId=equipment,symptom="vibration"});Assert.Equal(id,(await duplicate.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jobId").GetGuid());
            Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync("/api/jobs",new{equipmentId=equipment,symptom="changed"})).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/api/jobs",new{equipmentId=equipment,symptom="vibration",actor="supervisor"})).StatusCode);
            using(var stream=await client.GetAsync($"/api/jobs/{id}/events",HttpCompletionOption.ResponseHeadersRead))
            {
                Assert.Equal("text/event-stream",stream.Content.Headers.ContentType!.MediaType);
                using var reader=new StreamReader(await stream.Content.ReadAsStreamAsync());Assert.Equal("event: Job",await reader.ReadLineAsync());
            }
            Assert.Equal(ReasoningJobStatus.Queued,(await app.Services.GetRequiredService<IReasoningJobStore>().GetAsync(id,default))!.Status);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('b',32));
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/jobs/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync($"/api/jobs/{id}/events")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsync($"/api/jobs/{id}/cancel",null)).StatusCode);
            client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('a',32));
            var cancelled=await client.PostAsync($"/api/jobs/{id}/cancel",null);Assert.Equal(HttpStatusCode.OK,cancelled.StatusCode);
            Assert.Equal("Cancelled",(await cancelled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());
            var cancelledEvents=await client.GetStringAsync($"/api/jobs/{id}/events");
            Assert.Contains("event: Result",cancelledEvents);Assert.Contains("\"ExecutionId\":null",cancelledEvents);
            Assert.Null(await app.Services.GetRequiredService<IReasoningJobStore>().ClaimAsync("worker",[equipment],TimeSpan.FromSeconds(30),default));
            client.DefaultRequestHeaders.Remove("Idempotency-Key");client.DefaultRequestHeaders.Add("Idempotency-Key","revoked-actor");
            Assert.Equal(HttpStatusCode.Accepted,(await client.PostAsJsonAsync("/api/jobs",new{equipmentId=equipment,symptom="vibration"})).StatusCode);
            var claimed=(await app.Services.GetRequiredService<IReasoningJobStore>().ClaimAsync("worker",[equipment],TimeSpan.FromSeconds(30),default))!;
            Assert.Equal("ar-EG",claimed.Request.ResponseCulture);
            app.Services.GetRequiredService<IConfiguration>()["Authentication:Credentials:0:Permissions"]="read";
            Assert.Throws<UnauthorizedAccessException>(()=>app.Services.GetRequiredService<IReasoningJobExecutionScope>().Enter(claimed));
            await app.StopAsync();
        }
        finally {File.Delete(path);}
    }
}
