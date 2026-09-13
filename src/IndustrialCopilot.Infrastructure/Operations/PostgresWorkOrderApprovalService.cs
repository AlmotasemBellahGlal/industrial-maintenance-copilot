using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Domain.WorkOrders;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresWorkOrderApprovalService(PostgresWorkflowStore store,OperationalAccessPolicy policy,TimeProvider clock)
    : IWorkOrderApprovalService
{
    public async Task<WorkOrderReviewSnapshot?> GetReviewAsync(Guid workOrderId,string actorId,CancellationToken ct)
    {
        if(workOrderId==Guid.Empty) throw new ArgumentException("Identity required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ct.ThrowIfCancellationRequested();
        if(!await policy.CanReadWorkOrderAsync(workOrderId,actorId,ct)) return null;
        var stored=await store.GetWorkOrderAsync(workOrderId,ct);
        return stored is null?null:Snapshot(stored.Order,stored.ConcurrencyToken);
    }

    public async Task<ApprovalOperationResult> SubmitForReviewAsync(SubmitWorkOrderReviewRequest request,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        if(!await policy.CanSubmitAsync(request,ct)) return Failure(ApprovalOperationOutcome.Forbidden);
        return await Mutate(request.Target,null,ct);
    }

    public async Task<ApprovalOperationResult> RecordDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        var denied=await policy.ValidateDecisionAsync(request,ct);
        if(denied is not null)
        {
            if(denied is not (ApprovalOperationOutcome.Forbidden or ApprovalOperationOutcome.InvalidEdit or ApprovalOperationOutcome.SafetyValidationFailed))
                throw new InvalidOperationException("Invalid trusted policy result.");
            return Failure(denied.Value);
        }
        return await Mutate(request.Target,request,ct);
    }

    private Task<ApprovalOperationResult> Mutate(WorkOrderReviewTarget target,RecordWorkOrderDecisionRequest? decision,CancellationToken ct) =>
        Run(store.Source,async(c,t) =>
        {
            var current=await PostgresWorkflowStore.Load(c,t,target.WorkOrderId,true,ct);
            if(current is null) return Failure(ApprovalOperationOutcome.NotFound);
            var order=current.Order;
            if(await PostgresWorkflowStore.IsReserved(c,t,order.Id,ct)) return Failure(ApprovalOperationOutcome.Conflict);
            if(current.ConcurrencyToken!=target.ConcurrencyToken || order.Revision!=target.Revision) return Failure(ApprovalOperationOutcome.Conflict);
            if(order.Status!=(decision is null?WorkOrderStatus.Draft:WorkOrderStatus.PendingApproval)) return Failure(ApprovalOperationOutcome.InvalidState);
            if(order.SafetyAssessmentRevision!=order.Revision) return Failure(ApprovalOperationOutcome.SafetyValidationFailed);
            if(decision is null) order.SubmitForApproval(target.Revision);
            else
            {
                var at=clock.GetUtcNow();
                switch(decision.Kind)
                {
                    case ApprovalDecisionKind.Approve: order.Approve(decision.ActorId,target.Revision,at); break;
                    case ApprovalDecisionKind.Reject: order.Reject(decision.ActorId,target.Revision,at); break;
                    case ApprovalDecisionKind.EditAndApprove:
                        order.EditAndApprove(target.Revision,decision.EditedScope!.Content,decision.EditedScope.SafetyPrerequisites,decision.ActorId,at); break;
                    default: throw new ArgumentException("Unknown decision.");
                }
            }
            var token=await PostgresWorkflowStore.Save(c,t,order,current.MaintenanceRunId,current.ConcurrencyToken,ct);
            if(token is not null && decision is not null)
                await Execute(c,t,"INSERT INTO operations.human_approvals(id,revision,token) VALUES(@id,@revision,@token)",ct,
                    ("id",order.Id),("revision",order.Revision),("token",Guid.Parse(token)));
            return token is null?Failure(ApprovalOperationOutcome.Conflict):ApprovalOperationResult.Applied(Snapshot(order,token));
        },ct);

    private static WorkOrderReviewSnapshot Snapshot(WorkOrder order,string token) =>
        new(new(order.Id,order.Revision,token),order.Content,order.Status,order.SafetyAssessmentRevision,order.SafetyPrerequisites,order.ApprovalHistory.LastOrDefault());
    private static ApprovalOperationResult Failure(ApprovalOperationOutcome outcome) => ApprovalOperationResult.Failed(outcome,outcome.ToString());
}
