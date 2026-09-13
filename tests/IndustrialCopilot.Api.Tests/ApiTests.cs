using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using IndustrialCopilot.Api;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Application.Tests.Reasoning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialCopilot.Api.Tests;

public class ApiTests
{
    private sealed class Harness : IAsyncDisposable,IEquipmentContextStore,IWorkflowInspection,IWorkOrderApprovalService,IActionAuthorization,ISafetyVerificationService,IDispatchAttemptStore,IExternalDispatch
    {
        internal Scenario Scenario=new(); internal WebApplication App=null!; internal HttpClient Client=null!;
        internal RecordWorkOrderDecisionRequest? Decision; internal int DispatchCalls; internal bool FailReview;
        public Harness(){Scenario.Retrieval.Fail=false;Scenario.Retrieval.WrongRevision=false;Scenario.Store.PublishConflict=false;Scenario.Trace.FailFinal=false;}
        internal async Task Start(string permissions="read,start,approve,verify,dispatch",int capacity=32)
        {
            App=ApiHost.Build(["--environment","Testing"],b=>
            {
                b.WebHost.UseUrls("http://127.0.0.1:0");b.Logging.ClearProviders();
                b.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{
                    ["Authentication:Credentials:0:Actor"]="authenticated-supervisor",["Authentication:Credentials:0:Secret"]=new('x',32),
                    ["Authentication:Credentials:0:Permissions"]=permissions,["Authentication:Credentials:0:EquipmentIds"]=Scenario.Candidate.EquipmentId.ToString(),["Streaming:Capacity"]=capacity.ToString()});
                b.Services.AddSingleton<IWorkflowStore>(Scenario.Store);b.Services.AddSingleton<IRunTraceStore>(Scenario.Trace);
                b.Services.AddSingleton(Scenario.Orchestrator());b.Services.AddSingleton<IExecutableSafetyPolicy>(Scenario.Policy());
                b.Services.AddSingleton<IReadOnlyList<ApprovedMaintenanceProcedure>>(new[]{new ApprovedMaintenanceProcedure(Scenario.Candidate,["Check vibration"],["Inspect seal"],"Inspect isolated pump",[Scenario.Requirement])});
                b.Services.AddSingleton<IEquipmentContextStore>(this);b.Services.AddSingleton<IWorkflowInspection>(this);
                b.Services.AddSingleton<IWorkOrderApprovalService>(this);b.Services.AddSingleton<IActionAuthorization>(this);b.Services.AddSingleton<ISafetyVerificationService>(this);
                b.Services.AddSingleton(new DispatchCoordinator(this,this,this));b.Services.AddSingleton<IDispatchAttemptStore>(this);b.Services.AddSingleton<ExecutableReviewService>();
            });
            await App.StartAsync();Client=new HttpClient{BaseAddress=new(App.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single())};Client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer",new string('x',32));
        }
        internal Task<HttpResponseMessage> StartRun(bool stream=false)=>Client.PostAsJsonAsync(stream?"/api/runs/stream":"/api/runs",new StartRunRequest(Scenario.Candidate.EquipmentId,"vibration"));
        public Task<EquipmentContext?> GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<EquipmentContext?>(new(id,[new(Scenario.Candidate.DocumentId,Scenario.Candidate.ManualRevisionId)]));
        public Task<RunLinks> GetLinksAsync(Guid id,CancellationToken ct)=>Task.FromResult(new RunLinks([],[]));
        public async Task<WorkOrderReviewSnapshot?> GetReviewAsync(Guid id,string actor,CancellationToken ct)
        {
            if(FailReview)throw new IOException("SECRET password SQL");
            var stored=await Scenario.Store.GetWorkOrderAsync(id,ct);if(stored is null)return null;
            return new(new(id,stored.Order.Revision,stored.ConcurrencyToken),stored.Order.Content,stored.Order.Status,stored.Order.SafetyAssessmentRevision,stored.Order.SafetyPrerequisites,stored.Order.ApprovalHistory.LastOrDefault());
        }
        public Task<ApprovalOperationResult> SubmitForReviewAsync(SubmitWorkOrderReviewRequest request,CancellationToken ct)=>Task.FromResult(ApprovalOperationResult.Failed(ApprovalOperationOutcome.InvalidState,"invalid"));
        public async Task<ApprovalOperationResult> RecordDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken ct)
        {
            Decision=request;var current=(await Scenario.Store.GetWorkOrderAsync(request.Target.WorkOrderId,ct))!;
            if(current.ConcurrencyToken!=request.Target.ConcurrencyToken)return ApprovalOperationResult.Failed(ApprovalOperationOutcome.Conflict,"conflict");
            var o=current.Order;
            if(request.Kind==IndustrialCopilot.Domain.WorkOrders.ApprovalDecisionKind.Approve)o.Approve(request.ActorId,o.Revision,DateTimeOffset.UtcNow);
            else if(request.Kind==IndustrialCopilot.Domain.WorkOrders.ApprovalDecisionKind.Reject)o.Reject(request.ActorId,o.Revision,DateTimeOffset.UtcNow);
            else o.EditAndApprove(o.Revision,request.EditedScope!.Content,request.EditedScope.SafetyPrerequisites,request.ActorId,DateTimeOffset.UtcNow);
            return ApprovalOperationResult.Applied(new(new(o.Id,o.Revision,"next-token"),o.Content,o.Status,o.SafetyAssessmentRevision,o.SafetyPrerequisites,o.ApprovalHistory.Last()));
        }
        public Task<bool> AuthorizeAsync(string actor,TrustedAction action,Guid resource,CancellationToken ct)=>Task.FromResult(actor=="authenticated-supervisor");
        public Task<VerificationResult> RecordAsync(WorkOrderReviewTarget target,Guid prerequisiteId,string actor,string evidence,bool satisfied,CancellationToken ct)=>Task.FromResult(new VerificationResult(DispatchGateOutcome.Ready,"verified-token"));
        public Task<DispatchReservation> ReserveAsync(DispatchCommand command,CancellationToken ct){DispatchCalls++;return Task.FromResult(new DispatchReservation(DispatchGateOutcome.NotDispatchable,null,DispatchGateFailure.Safety));}
        Task<DispatchAttempt?> IDispatchAttemptStore.GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<DispatchAttempt?>(null);
        public Task<DispatchAttemptSession?> OpenAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ExternalDispatchResult> SendAsync(DispatchAttempt attempt,CancellationToken ct)=>throw new Exception("External adapter must not be called");
        public Task<ExternalDispatchResult> ReconcileAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public async ValueTask DisposeAsync(){Client?.Dispose();if(App is not null){await App.StopAsync();await App.DisposeAsync();}}
    }
    [Fact]
    public async Task StartInspectAndCorrelationUseRealHttpAndSafeDtos()
    {
        await using var h=new Harness();h.Scenario.Success();await h.Start();var correlation=Guid.NewGuid();h.Client.DefaultRequestHeaders.Add("X-Correlation-ID",correlation.ToString());
        var response=await h.StartRun();Assert.Equal(HttpStatusCode.Created,response.StatusCode);
        var workflow=(await response.Content.ReadFromJsonAsync<WorkflowResponse>())!;Assert.Equal(correlation,workflow.CorrelationId);Assert.Equal(correlation,h.Scenario.Trace.Current!.CorrelationId);
        Assert.Equal(HttpStatusCode.OK,(await h.Client.GetAsync("/api/runs/"+workflow.RunId)).StatusCode);
        var review=await h.Client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+workflow.WorkOrderId);Assert.Equal("PendingApproval",review!.Status);Assert.Null(review.Requirements[0].VerifiedBy);
        var trace=await h.Client.GetStringAsync("/api/traces/"+workflow.ExecutionId);Assert.DoesNotContain("SOURCE_PRIVATE",trace);Assert.DoesNotContain("systemPrompt",trace);
    }
    [Theory]
    [InlineData("Approve","Approved")] [InlineData("Reject","Rejected")] [InlineData("EditAndApprove","Approved")]
    public async Task DecisionsUseHostActorAndEditedSafetyComesFromPolicy(string decision,string status)
    {
        await using var h=new Harness();h.Scenario.Success();await h.Start();var workflow=(await (await h.StartRun()).Content.ReadFromJsonAsync<WorkflowResponse>())!;
        var review=(await h.Client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+workflow.WorkOrderId))!;
        var requirements=decision=="EditAndApprove"?new[]{new RequirementRequest(h.Scenario.Requirement.Id,h.Scenario.Requirement.Description,true)}:null;
        var request=new DecisionRequest(review.Target,decision,decision=="EditAndApprove"?review.Content:null,requirements);
        var response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/decisions",request);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var json=await response.Content.ReadAsStringAsync();Assert.Contains(status,json);Assert.Equal("authenticated-supervisor",h.Decision!.ActorId);
        if(decision=="EditAndApprove")Assert.Equal(h.Scenario.Requirement.Id,Assert.Single(h.Decision.EditedScope!.SafetyPrerequisites).Id);
        var stale=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/decisions",new DecisionRequest(new(2,"stale"),"Approve"));Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
    }
    [Fact]
    public async Task SafetyClaimsCannotAuthorizeEditedScopeAndPermissionIsIndependent()
    {
        await using var h=new Harness();h.Scenario.Success();await h.Start();var workflow=(await (await h.StartRun()).Content.ReadFromJsonAsync<WorkflowResponse>())!;
        var review=(await h.Client.GetFromJsonAsync<ReviewResponse>("/api/work-orders/"+workflow.WorkOrderId))!;
        var response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/decisions",new DecisionRequest(review.Target,"EditAndApprove",review.Content,[]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);Assert.Null(h.Decision);
        response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/decisions",new{target=review.Target,decision="Approve",actorId="administrator"});Assert.Equal(HttpStatusCode.BadRequest,response.StatusCode);
        response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/verifications",new VerificationRequest(review.Target,h.Scenario.Requirement.Id,"physical observation",true));Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/dispatch",review.Target);Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);Assert.Equal(1,h.DispatchCalls);
        response=await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/submit",review.Target);Assert.Equal(HttpStatusCode.Conflict,response.StatusCode);
    }
    [Fact]
    public async Task MissingCredentialAndInsufficientPermissionFailClosedAndErrorsAreSafe()
    {
        await using var h=new Harness();h.Scenario.Success();await h.Start("read,start");var workflow=(await (await h.StartRun()).Content.ReadFromJsonAsync<WorkflowResponse>())!;
        Assert.Equal(HttpStatusCode.Forbidden,(await h.Client.PostAsJsonAsync($"/api/work-orders/{workflow.WorkOrderId}/dispatch",new TargetRequest(2,"token"))).StatusCode);
        h.FailReview=true;var response=await h.Client.GetAsync("/api/work-orders/"+workflow.WorkOrderId);Assert.Equal(HttpStatusCode.ServiceUnavailable,response.StatusCode);Assert.DoesNotContain("SECRET",await response.Content.ReadAsStringAsync());
        h.Client.DefaultRequestHeaders.Authorization=null;Assert.Equal(HttpStatusCode.Unauthorized,(await h.StartRun()).StatusCode);
    }
    [Theory]
    [InlineData("success","WaitingForApproval")] [InlineData("blocked","Blocked")] [InlineData("failure","Failed")]
    public async Task SseStreamsOrderedSafeProgress(string scenario,string terminal)
    {
        await using var h=new Harness();if(scenario=="success")h.Scenario.Success();else if(scenario=="blocked")h.Scenario.Llm.Output("{\"outcome\":\"InsufficientEvidence\"}");else h.Scenario.Llm.Script.Enqueue((_,_)=>throw new IOException("SECRET"));
        await h.Start();var response=await h.StartRun(true);Assert.Equal("text/event-stream",response.Content.Headers.ContentType!.MediaType);
        var text=await response.Content.ReadAsStringAsync();Assert.True(text.IndexOf("WorkflowStarted",StringComparison.Ordinal)<text.IndexOf("AgentStarted",StringComparison.Ordinal));Assert.Contains("event: "+terminal,text);Assert.DoesNotContain("SECRET",text);Assert.DoesNotContain("SOURCE_PRIVATE",text);
    }
    [Fact]
    public async Task StreamDisconnectCancelsActiveProviderAndBoundedOverflowDoesNotPublish()
    {
        await using(var h=new Harness())
        {
            var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            h.Scenario.Llm.Script.Enqueue(async(_,ct)=>{try{await Task.Delay(Timeout.Infinite,ct);}finally{cancelled.TrySetResult();}throw new Exception();});
            await h.Start();using var request=new HttpRequestMessage(HttpMethod.Post,"/api/runs/stream"){Content=JsonContent.Create(new StartRunRequest(h.Scenario.Candidate.EquipmentId,"vibration"))};
            using var response=await h.Client.SendAsync(request,HttpCompletionOption.ResponseHeadersRead);response.Dispose();
            await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));Assert.Equal(0,h.Scenario.Store.Publishes);
        }
        await using(var h=new Harness())
        {
            h.Scenario.Success();await h.Start(capacity:4);await h.StartRun(true);Assert.Equal(0,h.Scenario.Store.Publishes);
        }
    }
}
