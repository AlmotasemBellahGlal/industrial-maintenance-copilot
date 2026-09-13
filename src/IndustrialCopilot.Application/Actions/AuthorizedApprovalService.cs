using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;

namespace IndustrialCopilot.Application.Actions;

/// <summary>Adds authenticated action authorization while preserving the existing trusted consent/scope policy.</summary>
public sealed class AuthorizedApprovalService(IWorkOrderApprovalService inner,IActionAuthorization authorization) : IWorkOrderApprovalService
{
    public Task<WorkOrderReviewSnapshot?> GetReviewAsync(Guid id,string actor,CancellationToken ct)=>inner.GetReviewAsync(id,actor,ct);
    public Task<ApprovalOperationResult> SubmitForReviewAsync(SubmitWorkOrderReviewRequest request,CancellationToken ct)=>inner.SubmitForReviewAsync(request,ct);
    public async Task<ApprovalOperationResult> RecordDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request); ct.ThrowIfCancellationRequested();
        if(!await authorization.AuthorizeAsync(request.ActorId,TrustedAction.Approve,request.Target.WorkOrderId,ct))
            return ApprovalOperationResult.Failed(ApprovalOperationOutcome.Forbidden,"Approval authorization denied.");
        return await inner.RecordDecisionAsync(request,ct);
    }
}
