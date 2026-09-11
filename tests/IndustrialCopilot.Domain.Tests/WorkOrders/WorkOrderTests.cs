using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Domain.Tests.WorkOrders;

public class WorkOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static WorkOrder Create() => new(Guid.NewGuid(), "Inspect pump coupling");
    private static SafetyPrerequisite Requirement(bool mandatory = true) => new(Guid.NewGuid(), "Isolate power", mandatory);
    private static SafetyVerification Verification(bool satisfied = true) => new("technician-1", Now, "Isolation checked on site", satisfied);
    private static void Approve(WorkOrder order)
    {
        order.SubmitForApproval();
        order.Approve("supervisor-1", order.Revision, Now);
    }

    [Fact]
    public void CannotDispatchWithoutApproval()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void CannotDispatchWithoutSafetyAssessment()
    {
        var order = Create();
        Approve(order);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
        Assert.Null(order.SafetyAssessmentRevision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CannotDispatchWithUnsatisfiedOrUnverifiedMandatoryPrerequisite(bool unverified)
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        if (!unverified) order.VerifyPrerequisite(order.Revision, requirement.Id, Verification(false));
        Approve(order);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
    }

    [Fact]
    public void CanDispatchWithCurrentApprovalAndSatisfiedMandatoryPrerequisites()
    {
        var order = Create();
        var first = Requirement();
        var second = Requirement();
        order.AssessSafety(order.Revision, [first, second]);
        order.VerifyPrerequisite(order.Revision, first.Id, Verification());
        order.VerifyPrerequisite(order.Revision, second.Id, Verification());
        Approve(order);
        order.Dispatch();
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void ExplicitAssessedEmptySetCanDispatchWhenApproved()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        order.Dispatch();
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void UnverifiedOptionalPrerequisiteDoesNotBlockDispatch()
    {
        var order = Create();
        order.AssessSafety(order.Revision, [Requirement(false)]);
        Approve(order);
        order.Dispatch();
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void EditingCriticalDataInvalidatesApprovalAndAssessment()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        var oldRevision = order.Revision;
        order.EditDescription("Replace pump coupling");
        Assert.Equal(oldRevision + 1, order.Revision);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Null(order.SafetyAssessmentRevision);
        Assert.Equal(oldRevision, Assert.Single(order.ApprovalHistory).Revision);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
    }

    [Fact]
    public void StaleApprovalCannotApproveOrDispatchNewRevision()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        var oldRevision = order.Revision;
        order.EditDescription("Replace coupling");
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval();
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor-1", oldRevision, Now));
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Single(order.ApprovalHistory);
    }

    [Fact]
    public void RejectedWorkOrderCannotDispatch()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval();
        order.Reject("supervisor-1", order.Revision, Now);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.Rejected, order.Status);
        Assert.Equal(ApprovalDecisionKind.Reject, Assert.Single(order.ApprovalHistory).Kind);
    }

    [Fact]
    public void DispatchedWorkOrderCannotBeChangedOrDispatchedAgain()
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        Approve(order);
        order.Dispatch();
        Assert.Throws<InvalidOperationException>(() => order.EditDescription("Different work"));
        Assert.Throws<InvalidOperationException>(() => order.AssessSafety(order.Revision, []));
        Assert.Throws<InvalidOperationException>(() => order.VerifyPrerequisite(order.Revision, requirement.Id, Verification(false)));
        Assert.Throws<InvalidOperationException>(() => order.RevokeVerification(order.Revision, requirement.Id));
        Assert.Throws<InvalidOperationException>(order.SubmitForApproval);
        Assert.Throws<InvalidOperationException>(() => order.EditAndApprove("Different work", "supervisor-1", order.Revision, Now));
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
        Assert.Equal("Inspect pump coupling", order.Description);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DispatchRechecksVerificationAfterApproval(bool revoke)
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        Approve(order);
        if (revoke) order.RevokeVerification(order.Revision, requirement.Id);
        else order.VerifyPrerequisite(order.Revision, requirement.Id, Verification(false));
        Assert.Throws<InvalidOperationException>(order.Dispatch);
    }

    [Fact]
    public void ReplacingRequirementsInvalidatesApprovalAndClearsVerification()
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        Approve(order);
        var oldRevision = order.Revision;
        order.AssessSafety(order.Revision, order.SafetyPrerequisites);
        Assert.Equal(oldRevision + 1, order.Revision);
        Assert.Equal(order.Revision, order.SafetyAssessmentRevision);
        Assert.Equal(SafetyPrerequisiteStatus.Unverified, Assert.Single(order.SafetyPrerequisites).Status);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
    }

    [Fact]
    public void InvalidAssessmentAndEditsDoNotPartiallyMutateState()
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        Approve(order);
        var revision = order.Revision;
        Assert.Throws<ArgumentException>(() => order.AssessSafety(revision, [requirement, requirement]));
        Assert.Throws<ArgumentException>(() => order.AssessSafety(revision, [null!]));
        Assert.Throws<ArgumentException>(() => order.EditDescription(" "));
        Assert.Throws<ArgumentException>(() => order.VerifyPrerequisite(revision, Guid.NewGuid(), Verification()));
        Assert.Throws<InvalidOperationException>(() => order.AssessSafety(revision - 1, []));
        Assert.Equal(revision, order.Revision);
        Assert.Equal(revision, order.SafetyAssessmentRevision);
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
        Assert.Equal(requirement.Id, Assert.Single(order.SafetyPrerequisites).Id);
        Assert.Single(order.ApprovalHistory);
    }

    [Fact]
    public void InvalidEditAndApproveDoesNotPartiallyMutateState()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval();
        var revision = order.Revision;
        Assert.Throws<ArgumentException>(() => order.EditAndApprove("New work", " ", revision, Now));
        Assert.Equal("Inspect pump coupling", order.Description);
        Assert.Equal(revision, order.Revision);
        Assert.Equal(revision, order.SafetyAssessmentRevision);
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Empty(order.ApprovalHistory);
    }

    [Fact]
    public void EditAndApproveRecordsNewRevisionButRequiresFreshSafetyAssessment()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval();
        order.EditAndApprove("New work", "supervisor-1", order.Revision, Now);
        var decision = Assert.Single(order.ApprovalHistory);
        Assert.Equal(order.Revision, decision.Revision);
        Assert.Equal(ApprovalDecisionKind.EditAndApprove, decision.Kind);
        Assert.Null(order.SafetyAssessmentRevision);
        Assert.Throws<InvalidOperationException>(order.Dispatch);
        order.AssessSafety(order.Revision, []);
        Approve(order);
        order.Dispatch();
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void AggregateDefensivelyOwnsInputsAndCollections()
    {
        var order = Create();
        var requirement = Requirement();
        var input = new List<SafetyPrerequisite> { requirement };
        order.AssessSafety(order.Revision, input);
        input.Clear();
        Assert.Single(order.SafetyPrerequisites);
        Assert.Throws<NotSupportedException>(() => ((IList<SafetyPrerequisite>)order.SafetyPrerequisites).Clear());
        Approve(order);
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovalDecision>)order.ApprovalHistory).Clear());
    }

    [Fact]
    public void InvalidValuesAndInvalidReviewLeaveOrderUnchanged()
    {
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.Empty, "Work"));
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.NewGuid(), " "));
        Assert.Throws<ArgumentException>(() => new SafetyPrerequisite(Guid.Empty, "Isolate", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification(" ", Now, "Evidence", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification("tech", default, "Evidence", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification("tech", Now, " ", true));
        var order = Create();
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor", order.Revision, Now));
        order.SubmitForApproval();
        Assert.Throws<ArgumentException>(() => order.Approve(" ", order.Revision, Now));
        Assert.Throws<ArgumentException>(() => order.Approve("supervisor", order.Revision, default));
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Empty(order.ApprovalHistory);
    }
}
