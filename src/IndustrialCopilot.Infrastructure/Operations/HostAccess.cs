using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Application.Reasoning;
using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed record HostIdentity(string Actor,IReadOnlySet<string> Permissions,IReadOnlySet<Guid> Equipment);
/// <summary>Host-authenticated identity plus resource-scoped permissions. Never populated from tool/request bodies.</summary>
public sealed class HostAccess(Func<HostIdentity?> identity,PostgresWorkflowStore store,NpgsqlDataSource source,
    IExecutableSafetyPolicy safety,IReadOnlyList<ApprovedMaintenanceProcedure> procedures)
    : OperationalAccessPolicy,IActionAuthorization,ITrustedToolContextAccessor
{
    public TrustedToolContext? Current=>identity() is { } user?new(user.Actor,IndustrialCopilot.Application.Abstractions.Agents.AgentRole.SymptomMatcher,Correlation.Value??Guid.NewGuid()):null;
    public static readonly AsyncLocal<Guid?> Correlation=new();
    public bool Can(string permission,Guid equipment)=>identity() is {} user && user.Permissions.Contains(permission) && user.Equipment.Contains(equipment);
    private bool Actor(string actor)=>identity() is {} user && user.Actor==actor;
    public async Task<bool> AuthorizeAsync(string actor,TrustedAction action,Guid resource,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); if(!Actor(actor)) return false;
        var equipment=action switch {
            TrustedAction.ReadEquipment or TrustedAction.ValidateSafety=>resource,
            TrustedAction.ReadEvidence=>procedures.FirstOrDefault(p=>p.Candidate.DocumentId==resource)?.Candidate.EquipmentId,
            _=>(await store.GetWorkOrderAsync(resource,ct))?.Order.Content.EquipmentId };
        var permission=action switch{TrustedAction.Approve=>"approve",TrustedAction.VerifySafety=>"verify",TrustedAction.Dispatch=>"dispatch",_=>"read"};
        return equipment.HasValue && Can(permission,equipment.Value);
    }
    public override async Task<bool> CanReadWorkOrderAsync(Guid id,string actor,CancellationToken ct)=>Actor(actor) && (await store.GetWorkOrderAsync(id,ct)) is {} order && Can("read",order.Order.Content.EquipmentId);
    public override async Task<bool> CanSubmitAsync(SubmitWorkOrderReviewRequest request,CancellationToken ct)=>Actor(request.ActorId) && (await store.GetWorkOrderAsync(request.Target.WorkOrderId,ct)) is {} order && Can("start",order.Order.Content.EquipmentId);
    public override async Task<ApprovalOperationOutcome?> ValidateDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken ct)
    {
        if(!await AuthorizeAsync(request.ActorId,TrustedAction.Approve,request.Target.WorkOrderId,ct)) return ApprovalOperationOutcome.Forbidden;
        if(request.EditedScope is not {} edited) return null;
        var original=await store.GetWorkOrderAsync(request.Target.WorkOrderId,ct);
        if(original is null || original.Order.Content.EquipmentId!=edited.Content.EquipmentId) return ApprovalOperationOutcome.InvalidEdit;
        var assessment=await safety.AssessAsync(edited.Content,ct);
        if(!assessment.CanProceed || !assessment.Requirements.Select(p=>(p.Id,p.Description,p.IsMandatory)).SequenceEqual(edited.SafetyPrerequisites.Select(p=>(p.Id,p.Description,p.IsMandatory))))
            return ApprovalOperationOutcome.SafetyValidationFailed;
        return null;
    }
    public override async Task<bool> CanAccessTraceAsync(Guid executionId,bool write,CancellationToken ct)
    {
        if(identity() is not {} user) return false;
        // Writes are from trusted Application observers; no HTTP endpoint accepts trace snapshots.
        if(write) return user.Permissions.Contains("start") || user.Permissions.Contains("dispatch");
        await using var command=source.CreateCommand("SELECT r.equipment_id FROM operations.traces t JOIN operations.runs r ON r.id=t.run_id WHERE t.execution_id=@id");
        command.Parameters.AddWithValue("id",executionId);
        return await command.ExecuteScalarAsync(ct) is Guid equipment && Can("read",equipment);
    }
}
