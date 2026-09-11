using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

/// <summary>A snapshot of persisted work-order state, not an authorization credential.</summary>
/// <remarks>Construction checks structural consistency, not provenance or actor authorization.</remarks>
public sealed record WorkOrderReviewSnapshot
{
    public WorkOrderReviewTarget Target { get; }
    public WorkOrderContent Content { get; }
    public WorkOrderStatus Status { get; }
    public int? SafetyAssessmentRevision { get; }
    public IReadOnlyList<SafetyPrerequisite> SafetyPrerequisites { get; }
    /// <summary>Latest historical decision, which may concern an older revision of a draft or pending order.</summary>
    public ApprovalDecision? LatestDecision { get; }

    public WorkOrderReviewSnapshot(WorkOrderReviewTarget target, WorkOrderContent content,
        WorkOrderStatus status, int? safetyAssessmentRevision,
        IReadOnlyList<SafetyPrerequisite> safetyPrerequisites, ApprovalDecision? latestDecision = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(content);
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(safetyPrerequisites);
        var snapshot = safetyPrerequisites.ToArray();
        if (snapshot.Any(p => p is null) || snapshot.Select(p => p.Id).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Requirements must have unique identities and no null entries.", nameof(safetyPrerequisites));
        if (safetyAssessmentRevision is not null && safetyAssessmentRevision != target.Revision)
            throw new ArgumentException("Assessment must concern the current revision.", nameof(safetyAssessmentRevision));
        if (safetyAssessmentRevision is null && (snapshot.Length != 0 || status != WorkOrderStatus.Draft))
            throw new ArgumentException("Only an unassessed draft with no requirements may lack an assessment.");
        if (latestDecision is not null && latestDecision.Revision > target.Revision)
            throw new ArgumentException("Decision cannot concern a future revision.", nameof(latestDecision));
        if (status is WorkOrderStatus.Draft or WorkOrderStatus.PendingApproval)
        {
            if (latestDecision?.Revision == target.Revision)
                throw new ArgumentException("An undecided revision cannot have a current decision.", nameof(latestDecision));
        }
        else
        {
            if (latestDecision is null || latestDecision.Revision != target.Revision)
                throw new ArgumentException("A current decision is required.", nameof(latestDecision));
            var approved = latestDecision.Kind is ApprovalDecisionKind.Approve or ApprovalDecisionKind.EditAndApprove;
            if (status == WorkOrderStatus.Rejected ? latestDecision.Kind != ApprovalDecisionKind.Reject : !approved)
                throw new ArgumentException("Decision must agree with work order status.", nameof(latestDecision));
        }
        if (status == WorkOrderStatus.Dispatched && snapshot.Any(p => p.IsMandatory && p.Status != SafetyPrerequisiteStatus.Satisfied))
            throw new ArgumentException("Dispatched work requires satisfied mandatory prerequisites.", nameof(safetyPrerequisites));
        Target = target;
        Content = content;
        Status = status;
        SafetyAssessmentRevision = safetyAssessmentRevision;
        SafetyPrerequisites = Array.AsReadOnly(snapshot);
        LatestDecision = latestDecision;
    }
}
