using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Domain.WorkOrders;

public sealed class WorkOrder
{
    private readonly List<ApprovalDecision> decisions = [];
    private SafetyPrerequisite[] prerequisites = [];

    public Guid Id { get; }
    public string Description { get; private set; }
    public int Revision { get; private set; } = 1;
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Draft;
    public int? SafetyAssessmentRevision { get; private set; }
    public IReadOnlyList<SafetyPrerequisite> SafetyPrerequisites => Array.AsReadOnly(prerequisites);
    public IReadOnlyList<ApprovalDecision> ApprovalHistory => decisions.AsReadOnly();

    public WorkOrder(Guid id, string description)
    {
        if (id == Guid.Empty) throw new ArgumentException("Work order identity is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Id = id;
        Description = description;
    }

    public void EditDescription(string description)
    {
        EnsureNotDispatched();
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        BeginNewRevision();
        Description = description;
    }

    // The caller must establish the complete requirement set through deterministic safety assessment.
    // An explicit empty set means the assessment found no requirements; absence of assessment does not.
    public void AssessSafety(int expectedRevision, IEnumerable<SafetyPrerequisite> requirements)
    {
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        ArgumentNullException.ThrowIfNull(requirements);
        var assessed = requirements.ToArray();
        if (assessed.Any(p => p is null)) throw new ArgumentException("Requirements cannot contain null.", nameof(requirements));
        if (assessed.Select(p => p.Id).Distinct().Count() != assessed.Length)
            throw new ArgumentException("Prerequisite identities must be unique.", nameof(requirements));
        // Never import verification from a different order or assessment.
        assessed = assessed.Select(p => p.WithVerification(null)).ToArray();
        if (SafetyAssessmentRevision is not null || Status != WorkOrderStatus.Draft)
            BeginNewRevision();
        prerequisites = assessed;
        SafetyAssessmentRevision = Revision;
    }

    public void VerifyPrerequisite(int expectedRevision, Guid prerequisiteId, SafetyVerification verification)
    {
        ArgumentNullException.ThrowIfNull(verification);
        SetVerification(expectedRevision, prerequisiteId, verification);
    }

    public void RevokeVerification(int expectedRevision, Guid prerequisiteId) =>
        SetVerification(expectedRevision, prerequisiteId, null);

    public void SubmitForApproval()
    {
        if (Status != WorkOrderStatus.Draft) throw new InvalidOperationException("Only a draft can be submitted.");
        Status = WorkOrderStatus.PendingApproval;
    }

    // Application/API must authorize the supervisor before calling review behavior.
    public void Approve(string supervisorId, int expectedRevision, DateTimeOffset decidedAt) =>
        Review(supervisorId, expectedRevision, decidedAt, ApprovalDecisionKind.Approve);

    public void Reject(string supervisorId, int expectedRevision, DateTimeOffset decidedAt) =>
        Review(supervisorId, expectedRevision, decidedAt, ApprovalDecisionKind.Reject);

    public void EditAndApprove(string description, string supervisorId, int expectedRevision, DateTimeOffset decidedAt)
    {
        EnsurePendingReview(expectedRevision);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        var decision = new ApprovalDecision(supervisorId, checked(Revision + 1), ApprovalDecisionKind.EditAndApprove, decidedAt);
        BeginNewRevision();
        Description = description;
        decisions.Add(decision);
        Status = WorkOrderStatus.Approved;
        // Changed scope requires a fresh safety assessment before dispatch (and renewed review if reassessed).
    }

    public void Dispatch()
    {
        if (Status != WorkOrderStatus.Approved || decisions.LastOrDefault() is not { } approval
            || approval.Revision != Revision || approval.Kind == ApprovalDecisionKind.Reject)
            throw new InvalidOperationException("Current supervisor approval is required.");
        if (SafetyAssessmentRevision != Revision)
            throw new InvalidOperationException("A current safety assessment is required.");
        if (prerequisites.Any(p => p.IsMandatory && p.Status != SafetyPrerequisiteStatus.Satisfied))
            throw new InvalidOperationException("Every mandatory prerequisite must be satisfied.");
        Status = WorkOrderStatus.Dispatched;
    }

    private void SetVerification(int expectedRevision, Guid id, SafetyVerification? verification)
    {
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        if (SafetyAssessmentRevision != Revision) throw new InvalidOperationException("A current safety assessment is required.");
        var index = Array.FindIndex(prerequisites, p => p.Id == id);
        if (index < 0) throw new ArgumentException("Unknown prerequisite.", nameof(id));
        prerequisites[index] = prerequisites[index].WithVerification(verification);
    }

    private void Review(string supervisorId, int expectedRevision, DateTimeOffset decidedAt, ApprovalDecisionKind kind)
    {
        EnsurePendingReview(expectedRevision);
        var decision = new ApprovalDecision(supervisorId, Revision, kind, decidedAt);
        decisions.Add(decision);
        Status = kind == ApprovalDecisionKind.Reject ? WorkOrderStatus.Rejected : WorkOrderStatus.Approved;
    }

    private void BeginNewRevision()
    {
        Revision = checked(Revision + 1);
        Status = WorkOrderStatus.Draft;
        SafetyAssessmentRevision = null;
        prerequisites = [];
    }

    private void EnsurePendingReview(int expectedRevision)
    {
        EnsureRevision(expectedRevision);
        if (Status != WorkOrderStatus.PendingApproval) throw new InvalidOperationException("Work order must be pending approval.");
    }

    private void EnsureRevision(int expectedRevision)
    {
        if (expectedRevision != Revision) throw new InvalidOperationException("Work order revision has changed.");
    }

    private void EnsureNotDispatched()
    {
        if (Status == WorkOrderStatus.Dispatched) throw new InvalidOperationException("A dispatched work order cannot be changed.");
    }
}
