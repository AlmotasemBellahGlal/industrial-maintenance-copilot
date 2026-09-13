using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
namespace IndustrialCopilot.IntegrationTests.Operations;

public class HttpApprovalTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    [PostgresFact]
    public async Task AuthenticatedHttpEditedApprovalVerificationAndDispatchUseAuthoritativePersistedScope()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);await new DispatchReceiverSchema(database.Source).ApplyAsync(default);
        var equipment=Guid.NewGuid();var manual=Guid.NewGuid();var revision=Guid.NewGuid();var requirement=Guid.NewGuid();var path=Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(new[]{new{Candidate=new{EquipmentId=equipment,EquipmentName="pump",DocumentId=manual,ManualRevisionId=revision},DiagnosticInstructions=new[]{"inspect"},WorkInstructions=new[]{"repair"},WorkOrderDescription="reviewed",Requirements=new[]{new{Id=requirement,Description="authoritative isolation",IsMandatory=true}}}}));
            var store=new PostgresWorkflowStore(database.Source);var order=new WorkOrder(Guid.NewGuid(),new(equipment,manual,revision,"noise","old scope",[new(1,"old action")]));
            var old=new SafetyPrerequisite(Guid.NewGuid(),"old",true);order.AssessSafety(1,[old]);order.VerifyPrerequisite(2,old.Id,new("old-tech",DateTimeOffset.UtcNow,"old evidence",true));order.SubmitForApproval(2);
            var run=new MaintenanceRun(Guid.NewGuid(),equipment,"noise");run.Start();run.WaitForApproval();await store.TrySaveRunAsync(run,null,default);await store.TrySaveWorkOrderAsync(order,run.Id,null,default);
            await using var app=ApiHost.Build(["--environment","Testing"],b=>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["Safety:ProcedureFile"]=path,["ConnectionStrings:Operations"]=database.Source.ConnectionString,["ConnectionStrings:DispatchReceiver"]=database.Source.ConnectionString,["Dispatch:Adapter"]="PostgresInbox",["Authentication:Credentials:0:Actor"]="reviewer",["Authentication:Credentials:0:Secret"]=new('z',32),["Authentication:Credentials:0:Permissions"]="read,approve,verify,dispatch",["Authentication:Credentials:0:EquipmentIds"]=equipment.ToString()});
                b.Services.AddMaintenanceHost(b.Configuration,()=>HostAuthentication.Identity(new HttpContextAccessor().HttpContext),false);
            });
            await app.StartAsync();using var client=new HttpClient{BaseAddress=new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('z',32));
            var review=(await client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+order.Id))!;
            var content=new ContentRequest(equipment,manual,revision,"noise","reviewed",[new(1,"repair")]);
            var preview=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/edited-safety-preview",content);Assert.Equal(HttpStatusCode.OK,preview.StatusCode);
            var invalid=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/decisions",new DecisionRequest(review.Target,"EditAndApprove",content,[]));Assert.Equal(HttpStatusCode.UnprocessableEntity,invalid.StatusCode);
            var approved=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/decisions",new DecisionRequest(review.Target,"EditAndApprove",content,[new(requirement,"authoritative isolation",true)]));Assert.Equal(HttpStatusCode.OK,approved.StatusCode);
            var current=(await client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+order.Id))!;Assert.Equal(3,current.Target.Revision);Assert.Equal("Approved",current.Status);Assert.Null(Assert.Single(current.Requirements).VerifiedBy);
            var blocked=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/dispatch",current.Target);Assert.Equal(HttpStatusCode.UnprocessableEntity,blocked.StatusCode);
            var verified=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/verifications",new VerificationRequest(current.Target,requirement,"meter zero",true));Assert.Equal(HttpStatusCode.OK,verified.StatusCode);
            current=(await client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+order.Id))!;
            var delivered=await client.PostAsJsonAsync($"/api/work-orders/{order.Id}/dispatch",current.Target);Assert.Equal(HttpStatusCode.OK,delivered.StatusCode);var attempt=(await delivered.Content.ReadFromJsonAsync<DispatchResponse>())!;Assert.Equal("Confirmed",attempt.State);
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/dispatch-attempts/"+attempt.AttemptId)).StatusCode);
            Assert.Equal(WorkOrderStatus.Dispatched,(await store.GetWorkOrderAsync(order.Id,default))!.Order.Status);Assert.Equal(MaintenanceRunStatus.Completed,(await store.GetRunAsync(run.Id,default))!.Run.Status);
            await app.StopAsync();
        }
        finally{File.Delete(path);}
    }
}
