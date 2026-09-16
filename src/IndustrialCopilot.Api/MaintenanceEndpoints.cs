using System.Text.Json;
using System.Threading.Channels;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Infrastructure.Operations;

namespace IndustrialCopilot.Api;

public static class MaintenanceEndpoints
{
    private static T Service<T>(HttpContext c) where T:notnull=>c.RequestServices.GetRequiredService<T>();
    private static HostIdentity User(HttpContext c)=>HostAuthentication.Identity(c)??throw new ApiProblemException(401,"unauthenticated");
    private static void Permit(HttpContext c,string permission,Guid equipment)
    {var user=User(c);if(!user.Permissions.Contains(permission)||!user.Equipment.Contains(equipment))throw new ApiProblemException(403,"forbidden");}
    private static async Task<StoredWorkOrder> Order(HttpContext c,Guid id,string permission="read")
    {HttpValidation.Id(id);var order=await Service<IWorkflowStore>(c).GetWorkOrderAsync(id,c.RequestAborted)??throw new ApiProblemException(404,"not_found");Permit(c,permission,order.Order.Content.EquipmentId);return order;}
    public static void Map(WebApplication app)
    {
        var routes=app.MapGroup("/api").RequireAuthorization();
        routes.MapPost("/runs",async(StartRunRequest request,HttpContext c)=>
        {
            var input=await Prepare(request,c); var result=await Service<MaintenanceOrchestrator>(c).ExecuteAsync(input,c.RequestAborted);
            c.Response.Headers.Location="/api/runs/"+result.RunId;
            return Results.Json(new WorkflowResponse(result.RunId,result.WorkOrderId,input.ExecutionId,input.CorrelationId,result.Outcome.ToString(),result.Narrative,result.DegradationReason,result.Citations),statusCode:result.Outcome switch{MaintenanceReasoningOutcome.Proposed=>201,MaintenanceReasoningOutcome.Degraded or MaintenanceReasoningOutcome.DegradedRefused=>200,MaintenanceReasoningOutcome.Conflict=>409,MaintenanceReasoningOutcome.TimedOut=>504,MaintenanceReasoningOutcome.Failed=>503,_=>422});
        });
        routes.MapPost("/runs/stream",Stream).Produces(200,contentType:"text/event-stream");
        routes.MapGet("/runs/{id:guid}",async(Guid id,HttpContext c)=>
        {
            var run=await Service<IWorkflowStore>(c).GetRunAsync(id,c.RequestAborted)??throw new ApiProblemException(404,"not_found"); Permit(c,"read",run.Run.EquipmentId);
            var links=await Service<IWorkflowInspection>(c).GetLinksAsync(id,c.RequestAborted);
            return new RunResponse(id,run.Run.EquipmentId,run.Run.ReportedSymptom,run.Run.Status.ToString(),run.Run.IsCancellationRequested,links.WorkOrderIds,links.ExecutionIds);
        });
        routes.MapGet("/work-orders/{id:guid}",async(Guid id,HttpContext c)=>
        {
            await Order(c,id); var review=await Service<IWorkOrderApprovalService>(c).GetReviewAsync(id,User(c).Actor,c.RequestAborted)??throw new ApiProblemException(404,"not_found"); return ReviewResponse.From(review);
        });
        routes.MapGet("/work-orders/{id:guid}/evidence",async(Guid id,HttpContext c)=>
        {
            var current=await Order(c,id); var proposal=await Service<IWorkflowStore>(c).GetProposalAsync(id,c.RequestAborted);
            // Original proposal citations are explicitly historical, never proof of edited scope.
            var evidence=proposal?.Actions.SelectMany(a=>a.Evidence).DistinctBy(e=>(e.DocumentId,e.ManualRevisionId,e.ChunkId)).Take(100).ToArray();
            return Results.Json(new {workOrderId=id,currentRevision=current.Order.Revision,source="original_proposal",evidence});
        });
        routes.MapPost("/work-orders/{id:guid}/submit",async(Guid id,TargetRequest request,HttpContext c)=>
        {await Order(c,id,"start");return Approval(await Service<IWorkOrderApprovalService>(c).SubmitForReviewAsync(new(request.Bind(id),User(c).Actor),c.RequestAborted));});
        routes.MapPost("/work-orders/{id:guid}/edited-safety-preview",async(Guid id,ContentRequest request,HttpContext c)=>
        {
            var original=await Order(c,id,"approve");var content=request.Bind();if(content.EquipmentId!=original.Order.Content.EquipmentId)throw new ApiProblemException(422,"invalid_edit");
            var assessment=await Service<IExecutableSafetyPolicy>(c).AssessAsync(content,c.RequestAborted);
            return Results.Json(new {assessment.CanProceed,requirements=assessment.Requirements.Select(p=>new RequirementRequest(p.Id,p.Description,p.IsMandatory))},statusCode:assessment.CanProceed?200:422);
        });
        routes.MapPost("/work-orders/{id:guid}/decisions",async(Guid id,DecisionRequest request,HttpContext c)=>
        {
            await Order(c,id,"approve");ArgumentNullException.ThrowIfNull(request.Target);var target=request.Target.Bind(id);var actor=User(c).Actor;
            if(request.Decision=="EditAndApprove")
            {
                if(request.EditedContent is null || request.ReviewedRequirements is null || request.ReviewedRequirements.Count>64 || request.ReviewedRequirements.Any(p=>p is null))throw new ArgumentException();
                return Approval(await Service<ExecutableReviewService>(c).EditAndApproveAsync(target,request.EditedContent.Bind(),request.ReviewedRequirements.Select(p=>p.Bind()).ToArray(),actor,c.RequestAborted));
            }
            if(request.EditedContent is not null || request.ReviewedRequirements is not null)throw new ArgumentException();
            var decision=request.Decision switch{"Approve"=>RecordWorkOrderDecisionRequest.Approve(target,actor),"Reject"=>RecordWorkOrderDecisionRequest.Reject(target,actor),_=>throw new ArgumentException()};
            return Approval(await Service<IWorkOrderApprovalService>(c).RecordDecisionAsync(decision,c.RequestAborted));
        });
        routes.MapPost("/work-orders/{id:guid}/verifications",async(Guid id,VerificationRequest request,HttpContext c)=>
        {
            await Order(c,id,"verify");ArgumentNullException.ThrowIfNull(request.Target);HttpValidation.Text(request.Evidence,4000);
            var result=await Service<ISafetyVerificationService>(c).RecordAsync(request.Target.Bind(id),request.PrerequisiteId,User(c).Actor,request.Evidence,request.Satisfied,c.RequestAborted);
            return Results.Json(new {outcome=result.Outcome.ToString(),result.ConcurrencyToken},statusCode:GateStatus(result.Outcome));
        });
        routes.MapPost("/work-orders/{id:guid}/dispatch",async(Guid id,TargetRequest request,HttpContext c)=>
        {await Order(c,id,"dispatch");return Dispatch(await Service<DispatchCoordinator>(c).DispatchAsync(new(request.Bind(id),User(c).Actor),c.RequestAborted));});
        routes.MapGet("/dispatch-attempts/{id:guid}",async(Guid id,HttpContext c)=>
        {
            var attempt=await Service<IDispatchAttemptStore>(c).GetAsync(id,c.RequestAborted)??throw new ApiProblemException(404,"not_found");await Order(c,attempt.WorkOrderId);return Dispatch(new(DispatchGateOutcome.Ready,attempt),true);
        });
        routes.MapGet("/traces/{id:guid}",async(Guid id,HttpContext c)=>
        {
            var trace=await Service<IRunTraceStore>(c).GetAsync(id,c.RequestAborted)??throw new ApiProblemException(404,"not_found");
            return new TraceResponse(trace.ExecutionId,trace.CorrelationId,trace.MaintenanceRunId,trace.Steps.Take(512).Select(s=>new TraceStepResponse(s.Kind.ToString(),s.OperationName,s.Status.ToString(),s.Error?.Code,s.StartedAt,s.CompletedAt,s.StepId,s.ParentStepId)).ToArray());
        });
    }
    private static IResult Approval(ApprovalOperationResult result)=>Results.Json(new {outcome=result.Outcome.ToString(),review=result.Snapshot is {} s?ReviewResponse.From(s):null},statusCode:result.Outcome switch{ApprovalOperationOutcome.Applied=>200,ApprovalOperationOutcome.Forbidden=>403,ApprovalOperationOutcome.NotFound=>404,ApprovalOperationOutcome.Conflict or ApprovalOperationOutcome.InvalidState=>409,_=>422});
    private static int GateStatus(DispatchGateOutcome outcome)=>outcome switch{DispatchGateOutcome.Ready=>200,DispatchGateOutcome.Forbidden=>403,DispatchGateOutcome.NotFound=>404,DispatchGateOutcome.Conflict=>409,_=>422};
    private static IResult Dispatch(DispatchReservation result,bool inspection=false)=>Results.Json(new DispatchResponse(result.Attempt?.Id,result.Attempt?.WorkOrderId,result.Attempt?.Revision,result.Outcome.ToString(),result.Attempt?.State.ToString(),result.Attempt?.ExternalReference,result.Failure?.ToString()),statusCode:inspection?200:result.Attempt?.State switch{DispatchAttemptState.Pending or DispatchAttemptState.Uncertain=>202,DispatchAttemptState.DefinitivelyFailed=>422,_=>GateStatus(result.Outcome)});
    internal static async Task<MaintenanceReasoningRequest> Prepare(StartRunRequest request,HttpContext c)
    {
        HttpValidation.Id(request.EquipmentId);HttpValidation.Text(request.Symptom,2000);Permit(c,"start",request.EquipmentId);Permit(c,"read",request.EquipmentId);
        var equipment=await Service<IEquipmentContextStore>(c).GetAsync(request.EquipmentId,c.RequestAborted)??throw new ApiProblemException(404,"equipment_not_found");
        var candidates=Service<IReadOnlyList<ApprovedMaintenanceProcedure>>(c).Where(p=>p.Candidate.EquipmentId==request.EquipmentId && equipment.Manuals.Any(m=>m.ManualId==p.Candidate.DocumentId && m.ManualRevisionId==p.Candidate.ManualRevisionId)).Select(p=>p.Candidate).ToArray();
        if(candidates.Length is <1 or >8)throw new ApiProblemException(422,"procedure_coverage_missing");
        return new(Guid.NewGuid(),(Guid)c.Items["correlation"]!,Guid.NewGuid(),Guid.NewGuid(),new(request.Symptom,candidates,[]),System.Globalization.CultureInfo.CurrentUICulture.Name);
    }
    private sealed class ProgressWriter(ChannelWriter<MaintenanceProgress> writer,CancellationTokenSource lifetime) : IProgress<MaintenanceProgress>
    {public void Report(MaintenanceProgress value){if(!writer.TryWrite(value))lifetime.Cancel();}}
    private static async Task Stream(StartRunRequest request,HttpContext c)
    {
        var input=await Prepare(request,c);var options=Service<StreamingOptions>(c);
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);
        var channel=Channel.CreateBounded<MaintenanceProgress>(new BoundedChannelOptions(options.Capacity){FullMode=BoundedChannelFullMode.Wait,SingleReader=true,SingleWriter=true});
        MaintenanceReasoningResult? result=null;
        async Task Produce()
        {
            try {result=await Service<MaintenanceOrchestrator>(c).ExecuteAsync(input,lifetime.Token,new ProgressWriter(channel.Writer,lifetime));}
            catch(OperationCanceledException) when(lifetime.IsCancellationRequested) { }
            catch {channel.Writer.TryWrite(new(MaintenanceProgressKind.Failed,input.RunId));}
            finally{channel.Writer.TryComplete();}
        }
        c.Response.ContentType="text/event-stream";c.Response.Headers.CacheControl="no-store";c.Response.Headers["X-Accel-Buffering"]="no";
        var producer=Produce();
        try
        {
            await foreach(var progress in channel.Reader.ReadAllAsync(c.RequestAborted))
                await Write(progress.Kind.ToString(),new {input.CorrelationId,input.ExecutionId,progress});
            if(result is not null)await Write("Result",new WorkflowResponse(result.RunId,result.WorkOrderId,input.ExecutionId,input.CorrelationId,result.Outcome.ToString(),result.Narrative,result.DegradationReason,result.Citations));
        }
        finally{lifetime.Cancel();await producer;}
        async Task Write(string kind,object value)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(c.RequestAborted);timeout.CancelAfter(options.WriteTimeout);
            await c.Response.WriteAsync("event: "+kind+"\ndata: "+JsonSerializer.Serialize(value)+"\n\n",timeout.Token);await c.Response.Body.FlushAsync(timeout.Token);
        }
    }
}
public sealed record StreamingOptions
{
    public int Capacity {get;} public TimeSpan WriteTimeout {get;}
    public StreamingOptions(int capacity=32,int writeTimeoutSeconds=10){if(capacity is <4 or >128 || writeTimeoutSeconds is <1 or >60)throw new ArgumentException("Invalid streaming limits.");Capacity=capacity;WriteTimeout=TimeSpan.FromSeconds(writeTimeoutSeconds);}
}
