using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

public sealed record ApprovalOperationResult
{
    public ApprovalOperationOutcome Outcome { get; }
    /// <summary>Resulting persisted state on Applied; otherwise an optional authorized current-state snapshot.</summary>
    public WorkOrderReviewSnapshot? Snapshot { get; }
    public string? Explanation { get; }

    private ApprovalOperationResult(ApprovalOperationOutcome outcome, WorkOrderReviewSnapshot? snapshot, string? explanation)
    {
        Outcome = outcome;
        Snapshot = snapshot;
        Explanation = explanation;
    }

    public static ApprovalOperationResult Applied(WorkOrderReviewSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Status is not (WorkOrderStatus.PendingApproval or WorkOrderStatus.Approved or WorkOrderStatus.Rejected))
            throw new ArgumentException("An approval operation must result in pending, approved, or rejected state.", nameof(snapshot));
        return new(ApprovalOperationOutcome.Applied, snapshot, null);
    }

    public static ApprovalOperationResult Failed(ApprovalOperationOutcome outcome, string explanation, WorkOrderReviewSnapshot? currentSnapshot = null)
    {
        if (!Enum.IsDefined(outcome) || outcome == ApprovalOperationOutcome.Applied)
            throw new ArgumentOutOfRangeException(nameof(outcome));
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        if (currentSnapshot is not null && outcome is ApprovalOperationOutcome.NotFound or ApprovalOperationOutcome.Forbidden)
            throw new ArgumentException("Unavailable targets must not disclose a snapshot.", nameof(currentSnapshot));
        return new(outcome, currentSnapshot, explanation);
    }
}
