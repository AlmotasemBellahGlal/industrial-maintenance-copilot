using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
namespace IndustrialCopilot.Application.Actions;

public sealed class ExecutableReviewService(IWorkflowStore store,IExecutableSafetyPolicy safety,IActionAuthorization authorization,IWorkOrderApprovalService approval)
{
    public async Task<ApprovalOperationResult> EditAndApproveAsync(WorkOrderReviewTarget target,WorkOrderContent content,
        IReadOnlyList<SafetyPrerequisite> reviewedRequirements,string actor,CancellationToken ct)
    {
        if(!await authorization.AuthorizeAsync(actor,TrustedAction.Approve,target.WorkOrderId,ct)) return Fail(ApprovalOperationOutcome.Forbidden);
        var current=await store.GetWorkOrderAsync(target.WorkOrderId,ct);
        if(current is null) return Fail(ApprovalOperationOutcome.NotFound);
        if(current.ConcurrencyToken!=target.ConcurrencyToken || current.Order.Revision!=target.Revision) return Fail(ApprovalOperationOutcome.Conflict);
        if(current.Order.Content.EquipmentId!=content.EquipmentId) return Fail(ApprovalOperationOutcome.InvalidEdit);
        var assessment=await safety.AssessAsync(content,ct);
        if(!assessment.CanProceed || !assessment.Requirements.Select(p=>(p.Id,p.Description,p.IsMandatory)).SequenceEqual(reviewedRequirements.Select(p=>(p.Id,p.Description,p.IsMandatory))))
            return Fail(ApprovalOperationOutcome.SafetyValidationFailed);
        return await approval.RecordDecisionAsync(RecordWorkOrderDecisionRequest.EditAndApprove(target,actor,new(content,assessment.Requirements)),ct);
    }
    private static ApprovalOperationResult Fail(ApprovalOperationOutcome outcome)=>ApprovalOperationResult.Failed(outcome,outcome.ToString());
}
