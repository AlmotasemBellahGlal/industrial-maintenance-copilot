using IndustrialCopilot.Application.Abstractions.AI;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;

namespace IndustrialCopilot.Application.Actions;

public enum ToolEffect { ReadOnly=1, Validation=2, ExternalSideEffect=3 }
public enum ToolExecutionOutcome { Completed=1, Forbidden=2, InvalidArguments=3, UnknownTool=4, Unavailable=5 }
/// <summary>Trusted host supplies context, never model arguments. Identity does not confer permission.</summary>
public sealed record TrustedToolContext(string ActorId,AgentRole? Role,Guid CorrelationId,
    DiagnosticPlan? Plan=null,WorkOrderProposal? Proposal=null);
public sealed record RegisteredTool(ToolDefinition Definition,ToolEffect Effect);
public sealed record ToolExecutionResult(ToolExecutionOutcome Outcome,
    IReadOnlyList<RetrievalResult>? Evidence=null,EquipmentContext? Equipment=null,
    SafetyAssessment? Assessment=null,DispatchReservation? Dispatch=null,DependencyFailureKind? Failure=null);

/// <summary>Fixed capability registry. Descriptions are not permissions; dispatch is host-only.</summary>
public sealed class TrustedToolExecutor(IRetrievalService retrieval,IEquipmentContextStore equipment,
    ISafetyPolicy safety,DispatchCoordinator dispatch,IActionAuthorization authorization,IRunTraceStore traces)
{
    public static IReadOnlyList<RegisteredTool> Tools { get; }=Array.AsReadOnly(new[] {
        Define("retrieve_manual_evidence",ToolEffect.ReadOnly,"Retrieve evidence from one manual revision.","""{"query":{"type":"string"},"topK":{"type":"integer","minimum":1,"maximum":100},"documentId":{"type":"string","format":"uuid"},"manualRevisionId":{"type":"string","format":"uuid"},"mode":{"type":"string","enum":["Keyword","Dense","Hybrid"]}}"""),
        Define("get_equipment_context",ToolEffect.ReadOnly,"Read persisted equipment and applicable manual identities.","""{"equipmentId":{"type":"string","format":"uuid"}}"""),
        Define("validate_safety_requirements",ToolEffect.Validation,"Assess the trusted current plan using deterministic safety policy.","{}"),
        Define("dispatch_approved_work_order",ToolEffect.ExternalSideEffect,"Host-only dispatch of an exact persisted approved scope.","""{"workOrderId":{"type":"string","format":"uuid"},"revision":{"type":"integer","minimum":1},"concurrencyToken":{"type":"string"}}""") });

    private static RegisteredTool Define(string name,ToolEffect effect,string description,string properties)
    {
        using var p=JsonDocument.Parse(properties);
        var required=JsonSerializer.Serialize(p.RootElement.EnumerateObject().Select(x=>x.Name));
        using var schema=JsonDocument.Parse("{\"type\":\"object\",\"additionalProperties\":false,\"properties\":"+properties+",\"required\":"+required+"}");
        return new(new ToolDefinition(name,description,schema.RootElement),effect);
    }
    public async Task<ToolExecutionResult> ExecuteAsync(ToolCall call,TrustedToolContext? context,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(call); ct.ThrowIfCancellationRequested();
        if(context is null || string.IsNullOrWhiteSpace(context.ActorId) || context.CorrelationId==Guid.Empty
            || (context.Role.HasValue && !Enum.IsDefined(context.Role.Value))) return new(ToolExecutionOutcome.Forbidden);
        var started=DateTimeOffset.UtcNow;
        var outcome=ToolExecutionOutcome.Unavailable;
        var observations=new List<string>();
        var name=Tools.FirstOrDefault(x=>x.Definition.Name==call.Name)?.Definition.Name;
        try
        {
            if(name is null) return new(outcome=ToolExecutionOutcome.UnknownTool);
            if(!Allowed(name,context.Role)) return new(outcome=ToolExecutionOutcome.Forbidden);
            ToolExecutionResult result;
            try { result=await Invoke(call,context,observations,ct); }
            catch(ArgumentException) { result=new(ToolExecutionOutcome.InvalidArguments); }
            catch(InvalidOperationException) { result=new(ToolExecutionOutcome.Unavailable); }
            if(result.Dispatch is { } delivery)
            {
                observations.Add("dispatch_gate_"+delivery.Outcome);
                if(delivery.Failure is { } reason) observations.Add("dispatch_gate_failure_"+reason);
                if(delivery.Attempt is { } attempt) observations.Add("dispatch_attempt_"+attempt.State);
            }
            outcome=result.Outcome; return result;
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
        catch(DependencyFailureException error) when(name=="retrieve_manual_evidence")
        {return new(outcome=ToolExecutionOutcome.Unavailable,Failure:error.Failure);}
        catch { return new(outcome=ToolExecutionOutcome.Unavailable); }
        finally
        {
            // Observational failure must not hide accepted delivery or replace caller cancellation.
            try
            {
                var end=DateTimeOffset.UtcNow;
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var root=Guid.NewGuid();
                var steps=new List<TraceStep> {
                    new(root,null,TraceOperationKind.Tool,name??"unknown_tool",started,
                        ct.IsCancellationRequested?TraceStepStatus.Cancelled:TraceStepStatus.Completed,end),
                    new(Guid.NewGuid(),root,TraceOperationKind.Tool,"result_"+outcome,started,TraceStepStatus.Completed,end)};
                steps.AddRange(observations.Select(o=>new TraceStep(Guid.NewGuid(),root,TraceOperationKind.Tool,o,started,TraceStepStatus.Completed,end)));
                var snapshot=new RunTraceSnapshot(Guid.NewGuid(),context.CorrelationId,null,1,steps);
                await traces.TrySaveAsync(snapshot,0,cleanup.Token);
            }
            catch { /* Durable dispatch audit is independent of traces. */ }
        }
    }
    private static bool Allowed(string name,AgentRole? role) => role is null || name switch {
        "retrieve_manual_evidence"=>role==AgentRole.SymptomMatcher,
        "get_equipment_context"=>role==AgentRole.SymptomMatcher,
        "validate_safety_requirements"=>role==AgentRole.DiagnosticSafetyPlanner,
        _=>false };

    private async Task<ToolExecutionResult> Invoke(ToolCall call,TrustedToolContext context,List<string> observations,CancellationToken ct)
    {
        var a=call.Arguments;
        var registered=Tools.Single(t=>t.Definition.Name==call.Name);
        var expected=registered.Definition.ArgumentsSchema.GetProperty("properties").EnumerateObject().Select(x=>x.Name).ToHashSet();
        var actual=a.EnumerateObject().Select(x=>x.Name).ToArray();
        if(actual.Length!=expected.Count || !expected.SetEquals(actual)) throw new ArgumentException("Unexpected arguments.");
        string Text(string key) { var v=a.GetProperty(key); if(v.ValueKind!=JsonValueKind.String || string.IsNullOrWhiteSpace(v.GetString())) throw new ArgumentException(); return v.GetString()!; }
        Guid Id(string key) { if(!Guid.TryParse(Text(key),out var id)||id==Guid.Empty) throw new ArgumentException(); return id; }
        int Number(string key) { var value=a.GetProperty(key); if(value.ValueKind!=JsonValueKind.Number || !value.TryGetInt32(out var n)||n<=0) throw new ArgumentException(); return n; }
        async Task<bool> Authorized(TrustedAction action,Guid id)
        {
            var allowed=await authorization.AuthorizeAsync(context.ActorId,action,id,ct);
            observations.Add("authorize_"+action+(allowed?"_granted":"_denied"));
            ct.ThrowIfCancellationRequested(); return allowed;
        }
        switch(call.Name)
        {
            case "retrieve_manual_evidence":
                var doc=Id("documentId"); var revision=Id("manualRevisionId"); var top=Number("topK");
                if(top>100 || !Enum.TryParse<RetrievalMode>(Text("mode"),false,out var mode) || !Enum.IsDefined(mode) || mode.ToString()!=Text("mode")) throw new ArgumentException();
                var query=new RetrievalQuery(Text("query"),top,doc,revision);
                if(!await Authorized(TrustedAction.ReadEvidence,doc)) return new(ToolExecutionOutcome.Forbidden);
                var found=await retrieval.RetrieveAsync(query,mode,ct);
                return new(ToolExecutionOutcome.Completed,Evidence:Array.AsReadOnly(found.Where(x=>x.DocumentId==doc && x.ManualRevisionId==revision).Take(top).ToArray()));
            case "get_equipment_context":
                var equipmentId=Id("equipmentId");
                if(!await Authorized(TrustedAction.ReadEquipment,equipmentId)) return new(ToolExecutionOutcome.Forbidden);
                return new(ToolExecutionOutcome.Completed,Equipment:await equipment.GetAsync(equipmentId,ct));
            case "validate_safety_requirements":
                if(context.Plan is null) throw new ArgumentException();
                if(!await Authorized(TrustedAction.ValidateSafety,context.Plan.SelectedCandidate.EquipmentId)) return new(ToolExecutionOutcome.Forbidden);
                return new(ToolExecutionOutcome.Completed,Assessment:await safety.AssessAsync(context.Plan,context.Proposal,ct));
            case "dispatch_approved_work_order":
                var target=new WorkOrderReviewTarget(Id("workOrderId"),Number("revision"),Text("concurrencyToken"));
                if(!await Authorized(TrustedAction.Dispatch,target.WorkOrderId)) return new(ToolExecutionOutcome.Forbidden);
                return new(ToolExecutionOutcome.Completed,Dispatch:await dispatch.DispatchAsync(new(target,context.ActorId),ct));
            default: return new(ToolExecutionOutcome.UnknownTool);
        }
    }
}
