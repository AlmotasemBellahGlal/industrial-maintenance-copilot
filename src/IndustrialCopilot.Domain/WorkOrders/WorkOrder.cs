using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Domain.WorkOrders;

public sealed class WorkOrder
{
    private readonly List<ApprovalDecision> decisions = [];
    private SafetyPrerequisite[] prerequisites = [];
    private bool materializingRequirements;

    public Guid Id { get; }
    public WorkOrderContent Content { get; private set; }
    public int Revision { get; private set; } = 1;
    public WorkOrderStatus Status { get; private set; } = WorkOrderStatus.Draft;
    public int? SafetyAssessmentRevision { get; private set; }
    public IReadOnlyList<SafetyPrerequisite> SafetyPrerequisites => Array.AsReadOnly(prerequisites);
    public IReadOnlyList<ApprovalDecision> ApprovalHistory => decisions.AsReadOnly();

    public WorkOrder(Guid id, WorkOrderContent content)
    {
        if (id == Guid.Empty) throw new ArgumentException("Work order identity is required.", nameof(id));
        ArgumentNullException.ThrowIfNull(content);
        Id = id;
        Content = content;
    }

    public void ReplaceContent(int expectedRevision, WorkOrderContent content)
    {
        EnsureNotMaterializingRequirements();
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        ArgumentNullException.ThrowIfNull(content);
        BeginNewRevision();
        Content = content;
    }

    // The caller must establish the complete requirement set through deterministic safety assessment.
    // An explicit empty set means the assessment found no requirements; absence of assessment does not.
    public void AssessSafety(int expectedRevision, IEnumerable<SafetyPrerequisite> requirements)
    {
        EnsureNotMaterializingRequirements();
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        var assessed = PrepareRequirements(requirements);
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        BeginNewRevision();
        prerequisites = assessed;
        SafetyAssessmentRevision = Revision;
    }

    public void VerifyPrerequisite(int expectedRevision, Guid prerequisiteId, SafetyVerification verification)
    {
        EnsureNotMaterializingRequirements();
        ArgumentNullException.ThrowIfNull(verification);
        SetVerification(expectedRevision, prerequisiteId, verification);
    }

    public void RevokeVerification(int expectedRevision, Guid prerequisiteId) =>
        SetVerification(expectedRevision, prerequisiteId, null);

    public void SubmitForApproval(int expectedRevision)
    {
        EnsureNotMaterializingRequirements();
        EnsureRevision(expectedRevision);
        EnsureAssessment();
        if (Status != WorkOrderStatus.Draft) throw new InvalidOperationException("Only a draft can be submitted.");
        Status = WorkOrderStatus.PendingApproval;
    }

    // Application/API must authorize the supervisor before calling review behavior.
    public void Approve(string supervisorId, int expectedRevision, DateTimeOffset decidedAt) =>
        Review(supervisorId, expectedRevision, decidedAt, ApprovalDecisionKind.Approve);

    public void Reject(string supervisorId, int expectedRevision, DateTimeOffset decidedAt) =>
        Review(supervisorId, expectedRevision, decidedAt, ApprovalDecisionKind.Reject);

    /// <summary>Installs the final scope already reviewed and approved by the supervisor.</summary>
    /// <remarks>
    /// The trusted caller must authenticate the supervisor and present the final content and
    /// authoritative requirements before obtaining approval. This operation cannot prove that consent.
    /// Verification never transfers to the resulting revision.
    /// </remarks>
    public void EditAndApprove(int expectedRevision, WorkOrderContent editedContent,
        IEnumerable<SafetyPrerequisite> authoritativeRequirements, string supervisorId, DateTimeOffset decidedAt)
    {
        EnsureNotMaterializingRequirements();
        EnsurePendingReview(expectedRevision);
        ArgumentNullException.ThrowIfNull(editedContent);
        var assessed = PrepareRequirements(authoritativeRequirements);
        EnsurePendingReview(expectedRevision);
        var nextRevision = checked(Revision + 1);
        var decision = new ApprovalDecision(supervisorId, nextRevision, ApprovalDecisionKind.EditAndApprove, decidedAt);
        decisions.EnsureCapacity(decisions.Count + 1);
        Content = editedContent;
        prerequisites = assessed;
        Revision = nextRevision;
        SafetyAssessmentRevision = nextRevision;
        decisions.Add(decision);
        Status = WorkOrderStatus.Approved;
    }

    public void Dispatch(int expectedRevision)
    {
        EnsureNotMaterializingRequirements();
        EnsureRevision(expectedRevision);
        if (Status != WorkOrderStatus.Approved || decisions.LastOrDefault() is not { } approval
            || approval.Revision != Revision || approval.Kind is not (ApprovalDecisionKind.Approve or ApprovalDecisionKind.EditAndApprove))
            throw new InvalidOperationException("Current supervisor approval is required.");
        if (SafetyAssessmentRevision != Revision)
            throw new InvalidOperationException("A current safety assessment is required.");
        if (prerequisites.Any(p => p.IsMandatory && p.Status != SafetyPrerequisiteStatus.Satisfied))
            throw new InvalidOperationException("Every mandatory prerequisite must be satisfied.");
        Status = WorkOrderStatus.Dispatched;
    }

    private void SetVerification(int expectedRevision, Guid id, SafetyVerification? verification)
    {
        EnsureNotMaterializingRequirements();
        EnsureNotDispatched();
        EnsureRevision(expectedRevision);
        if (SafetyAssessmentRevision != Revision) throw new InvalidOperationException("A current safety assessment is required.");
        var index = Array.FindIndex(prerequisites, p => p.Id == id);
        if (index < 0) throw new ArgumentException("Unknown prerequisite.", nameof(id));
        prerequisites[index] = prerequisites[index].WithVerification(verification);
    }

    private void Review(string supervisorId, int expectedRevision, DateTimeOffset decidedAt, ApprovalDecisionKind kind)
    {
        EnsureNotMaterializingRequirements();
        EnsurePendingReview(expectedRevision);
        EnsureAssessment();
        var decision = new ApprovalDecision(supervisorId, Revision, kind, decidedAt);
        decisions.Add(decision);
        Status = kind == ApprovalDecisionKind.Reject ? WorkOrderStatus.Rejected : WorkOrderStatus.Approved;
    }

    private SafetyPrerequisite[] PrepareRequirements(IEnumerable<SafetyPrerequisite> requirements)
    {
        EnsureNotMaterializingRequirements();
        materializingRequirements = true;
        try
        {
            ArgumentNullException.ThrowIfNull(requirements);
            var assessed = requirements.ToArray();
            if (assessed.Any(p => p is null)) throw new ArgumentException("Requirements cannot contain null.", nameof(requirements));
            if (assessed.Select(p => p.Id).Distinct().Count() != assessed.Length)
                throw new ArgumentException("Prerequisite identities must be unique.", nameof(requirements));
            return assessed.Select(p => p.WithVerification(null)).ToArray();
        }
        finally
        {
            materializingRequirements = false;
        }
    }

    private void EnsureNotMaterializingRequirements()
    {
        if (materializingRequirements)
            throw new InvalidOperationException("Work order cannot be mutated while safety requirements are being materialized.");
    }

    private void EnsureAssessment()
    {
        if (SafetyAssessmentRevision != Revision) throw new InvalidOperationException("A current safety assessment is required.");
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
