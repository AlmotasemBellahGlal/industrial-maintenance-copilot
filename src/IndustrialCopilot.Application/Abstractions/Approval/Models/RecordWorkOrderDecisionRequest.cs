using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

/// <summary>Human decision intent, not an already authorized or persisted decision.</summary>
public sealed record RecordWorkOrderDecisionRequest
{
    public WorkOrderReviewTarget Target { get; }
    /// <summary>Trusted host identity; execution must authenticate and authorize supervisor access.</summary>
    public string ActorId { get; }
    public ApprovalDecisionKind Kind { get; }
    public ReviewedWorkOrderScope? EditedScope { get; }

    private RecordWorkOrderDecisionRequest(WorkOrderReviewTarget target, string actorId,
        ApprovalDecisionKind kind, ReviewedWorkOrderScope? editedScope)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Target = target;
        ActorId = actorId;
        Kind = kind;
        EditedScope = editedScope;
    }

    public static RecordWorkOrderDecisionRequest Approve(WorkOrderReviewTarget target, string actorId) =>
        new(target, actorId, ApprovalDecisionKind.Approve, null);

    public static RecordWorkOrderDecisionRequest Reject(WorkOrderReviewTarget target, string actorId) =>
        new(target, actorId, ApprovalDecisionKind.Reject, null);

    public static RecordWorkOrderDecisionRequest EditAndApprove(WorkOrderReviewTarget target, string actorId, ReviewedWorkOrderScope finalReviewedScope)
    {
        ArgumentNullException.ThrowIfNull(finalReviewedScope);
        return new(target, actorId, ApprovalDecisionKind.EditAndApprove, finalReviewedScope);
    }
}
