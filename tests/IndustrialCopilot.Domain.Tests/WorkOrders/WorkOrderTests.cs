using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Domain.Tests.WorkOrders;

public class WorkOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
    private static WorkOrderContent Content(string description = "Inspect pump coupling") => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Vibration", description, [new(1, "Replace coupling")]);
    private static WorkOrder Create() => new(Guid.NewGuid(), Content());
    private static SafetyPrerequisite Requirement(bool mandatory = true) => new(Guid.NewGuid(), "Isolate power", mandatory);
    private static SafetyVerification Verification(bool satisfied = true) => new("technician-1", Now, "Isolation checked on site", satisfied);
    private static void Approve(WorkOrder order)
    {
        order.SubmitForApproval(order.Revision);
        order.Approve("supervisor-1", order.Revision, Now);
    }

    [Fact]
    public void CannotDispatchWithoutApproval()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
    }

    [Fact]
    public void CannotDispatchWithoutSafetyAssessment()
    {
        var order = Create();
        Assert.Throws<InvalidOperationException>(() => order.SubmitForApproval(order.Revision));
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor", order.Revision, Now));
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
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
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
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
        order.Dispatch(order.Revision);
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void ExplicitAssessedEmptySetCanDispatchWhenApproved()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        order.Dispatch(order.Revision);
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void UnverifiedOptionalPrerequisiteDoesNotBlockDispatch()
    {
        var order = Create();
        order.AssessSafety(order.Revision, [Requirement(false)]);
        Approve(order);
        order.Dispatch(order.Revision);
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
    }

    [Fact]
    public void EditingCriticalDataInvalidatesApprovalAndAssessment()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        var oldRevision = order.Revision;
        order.ReplaceContent(order.Revision, Content("Replace pump coupling"));
        Assert.Equal(oldRevision + 1, order.Revision);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Null(order.SafetyAssessmentRevision);
        Assert.Equal(oldRevision, Assert.Single(order.ApprovalHistory).Revision);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
    }

    [Fact]
    public void StaleApprovalCannotApproveOrDispatchNewRevision()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        var oldRevision = order.Revision;
        order.ReplaceContent(order.Revision, Content("Replace coupling"));
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor-1", oldRevision, Now));
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Single(order.ApprovalHistory);
    }

    [Fact]
    public void RejectedWorkOrderCannotDispatch()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        order.Reject("supervisor-1", order.Revision, Now);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
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
        order.Dispatch(order.Revision);
        Assert.Throws<InvalidOperationException>(() => order.ReplaceContent(order.Revision, Content("Different work")));
        Assert.Throws<InvalidOperationException>(() => order.AssessSafety(order.Revision, []));
        Assert.Throws<InvalidOperationException>(() => order.VerifyPrerequisite(order.Revision, requirement.Id, Verification(false)));
        Assert.Throws<InvalidOperationException>(() => order.RevokeVerification(order.Revision, requirement.Id));
        Assert.Throws<InvalidOperationException>(() => order.SubmitForApproval(order.Revision));
        Assert.Throws<InvalidOperationException>(() => order.EditAndApprove(order.Revision, Content("Different work"), [], "supervisor-1", Now));
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(WorkOrderStatus.Dispatched, order.Status);
        Assert.Equal("Inspect pump coupling", order.Content.Description);
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
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
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
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
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
        Assert.Throws<ArgumentNullException>(() => order.ReplaceContent(order.Revision, null!));
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
        order.SubmitForApproval(order.Revision);
        var revision = order.Revision;
        Assert.Throws<ArgumentException>(() => order.EditAndApprove(revision, Content("New work"), [], " ", Now));
        Assert.Equal("Inspect pump coupling", order.Content.Description);
        Assert.Equal(revision, order.Revision);
        Assert.Equal(revision, order.SafetyAssessmentRevision);
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Empty(order.ApprovalHistory);
    }

    [Fact]
    public void EditAndApproveRecordsOneReviewedRevisionWithCurrentAssessment()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        order.EditAndApprove(order.Revision, Content("New work"), [], "supervisor-1", Now);
        var decision = Assert.Single(order.ApprovalHistory);
        Assert.Equal(order.Revision, decision.Revision);
        Assert.Equal(ApprovalDecisionKind.EditAndApprove, decision.Kind);
        Assert.Equal(order.Revision, order.SafetyAssessmentRevision);
        Assert.Single(order.ApprovalHistory);
        order.Dispatch(order.Revision);
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
        Assert.Throws<ArgumentException>(() => new WorkOrder(Guid.Empty, Content()));
        Assert.Throws<ArgumentNullException>(() => new WorkOrder(Guid.NewGuid(), null!));
        Assert.Throws<ArgumentException>(() => new SafetyPrerequisite(Guid.Empty, "Isolate", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification(" ", Now, "Evidence", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification("tech", default, "Evidence", true));
        Assert.Throws<ArgumentException>(() => new SafetyVerification("tech", Now, " ", true));
        var order = Create();
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor", order.Revision, Now));
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        Assert.Throws<ArgumentException>(() => order.Approve(" ", order.Revision, Now));
        Assert.Throws<ArgumentException>(() => order.Approve("supervisor", order.Revision, default));
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Empty(order.ApprovalHistory);
    }

    [Fact]
    public void InitialAssessmentCreatesRevisionAndLifecycleDoesNotChangeIt()
    {
        var order = Create();
        Assert.Equal(1, order.Revision);
        order.AssessSafety(1, []);
        Assert.Equal(2, order.Revision);
        Assert.Equal(2, order.SafetyAssessmentRevision);
        Approve(order);
        order.Dispatch(2);
        Assert.Equal(2, order.Revision);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void EveryContentFieldChangeInvalidatesApproval(int field)
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        Approve(order);
        var revision = order.Revision;
        var old = order.Content;
        var changed = new WorkOrderContent(field == 0 ? Guid.NewGuid() : old.EquipmentId,
            field == 1 ? Guid.NewGuid() : old.ManualId, field == 2 ? Guid.NewGuid() : old.ManualRevisionId,
            field == 3 ? "Noise" : old.ReportedSymptom, field == 4 ? "New scope" : old.Description,
            field == 5 ? [new(1, "Other action")] : old.Actions);
        order.ReplaceContent(revision, changed);
        Assert.Equal(revision + 1, order.Revision);
        Assert.Same(changed, order.Content);
        Assert.Null(order.SafetyAssessmentRevision);
        Assert.Empty(order.SafetyPrerequisites);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Equal(revision, Assert.Single(order.ApprovalHistory).Revision);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(revision));
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
    }

    [Fact]
    public void VerificationChangesDoNotChangeApprovedScopeRevision()
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        Approve(order);
        var revision = order.Revision;
        order.VerifyPrerequisite(revision, requirement.Id, Verification());
        order.RevokeVerification(revision, requirement.Id);
        Assert.Equal(revision, order.Revision);
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(revision));
        order.VerifyPrerequisite(revision, requirement.Id, Verification());
        order.Dispatch(revision);
    }

    [Fact]
    public void EditAndApproveInstallsFinalScopeOnceAndRequiresFreshVerification()
    {
        var order = Create();
        var requirement = Requirement();
        var obsolete = Requirement();
        order.AssessSafety(order.Revision, [requirement, obsolete]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        order.SubmitForApproval(order.Revision);
        var revision = order.Revision;
        var edited = Content("Replace assembly");
        var added = new SafetyPrerequisite(Guid.NewGuid(), "Depressurize assembly", true);
        var requirements = new List<SafetyPrerequisite> { new(requirement.Id, "Check isolation indicator", false), added };
        order.EditAndApprove(revision, edited, requirements, "supervisor", Now);
        requirements.Clear();
        Assert.Same(edited, order.Content);
        Assert.Equal(revision + 1, order.Revision);
        Assert.Equal(order.Revision, order.SafetyAssessmentRevision);
        var decision = Assert.Single(order.ApprovalHistory);
        Assert.Equal(order.Revision, decision.Revision);
        Assert.Equal(ApprovalDecisionKind.EditAndApprove, decision.Kind);
        Assert.Equal("supervisor", decision.SupervisorId);
        Assert.Equal(Now, decision.DecidedAt);
        Assert.Collection(order.SafetyPrerequisites,
            first =>
            {
                Assert.Equal(requirement.Id, first.Id);
                Assert.Equal("Check isolation indicator", first.Description);
                Assert.False(first.IsMandatory);
                Assert.Null(first.Verification);
            },
            second =>
            {
                Assert.Equal(added.Id, second.Id);
                Assert.Equal("Depressurize assembly", second.Description);
                Assert.True(second.IsMandatory);
                Assert.Null(second.Verification);
            });
        Assert.DoesNotContain(order.SafetyPrerequisites, p => p.Id == obsolete.Id);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        order.VerifyPrerequisite(order.Revision, added.Id, Verification());
        order.Dispatch(order.Revision);
        Assert.Single(order.ApprovalHistory);
    }

    [Fact]
    public void InvalidFinalScopesAndThrowingEnumerationLeaveCompleteStateUnchanged()
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        Approve(order);
        order.ReplaceContent(order.Revision, Content());
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        order.SubmitForApproval(order.Revision);
        var content = order.Content;
        var revision = order.Revision;
        var requirements = order.SafetyPrerequisites.ToArray();
        var history = order.ApprovalHistory.ToArray();
        var enumerationFailure = new InvalidOperationException("Extraction failed");
        (Type ExceptionType, Action Attempt)[] attempts =
        [
            (typeof(ArgumentNullException), () => order.EditAndApprove(revision, null!, [], "supervisor", Now)),
            (typeof(ArgumentNullException), () => order.EditAndApprove(revision, Content(), null!, "supervisor", Now)),
            (typeof(ArgumentException), () => order.EditAndApprove(revision, Content(), [null!], "supervisor", Now)),
            (typeof(ArgumentException), () => order.EditAndApprove(revision, Content(), [requirement, requirement], "supervisor", Now)),
            (typeof(ArgumentException), () => order.EditAndApprove(revision, Content(), [], " ", Now)),
            (typeof(ArgumentException), () => order.EditAndApprove(revision, Content(), [], "supervisor", default)),
            (typeof(InvalidOperationException), () => order.EditAndApprove(revision, Content(), ThrowingRequirements(enumerationFailure), "supervisor", Now)),
            (typeof(InvalidOperationException), () => order.AssessSafety(revision, ThrowingRequirements(enumerationFailure))),
        ];
        foreach (var (exceptionType, attempt) in attempts)
        {
            var exception = Assert.Throws(exceptionType, attempt);
            if (exceptionType == typeof(InvalidOperationException)) Assert.Same(enumerationFailure, exception);
            Assert.Same(content, order.Content);
            Assert.Equal(revision, order.Revision);
            Assert.Equal(revision, order.SafetyAssessmentRevision);
            Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
            Assert.Equal(requirements, order.SafetyPrerequisites.ToArray());
            Assert.Equal(history, order.ApprovalHistory.ToArray());
        }
        order.Approve("supervisor", revision, Now);
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
    }

    private static IEnumerable<SafetyPrerequisite> ThrowingRequirements(Exception failure)
    {
        yield return Requirement();
        throw failure;
    }

    [Fact]
    public void StaleCommandsCannotChangeCurrentPendingReview()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        var stale = order.Revision;
        order.ReplaceContent(stale, Content());
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        var revision = order.Revision;
        Assert.Throws<InvalidOperationException>(() => order.Approve("supervisor", stale, Now));
        Assert.Throws<InvalidOperationException>(() => order.Reject("supervisor", stale, Now));
        Assert.Throws<InvalidOperationException>(() => order.EditAndApprove(stale, Content(), [], "supervisor", Now));
        Assert.Throws<InvalidOperationException>(() => order.ReplaceContent(stale, Content()));
        Assert.Throws<InvalidOperationException>(() => order.SubmitForApproval(stale));
        Assert.Equal(revision, order.Revision);
        Assert.Empty(order.ApprovalHistory);
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        order.Approve("supervisor", revision, Now);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(stale));
        Assert.Equal(WorkOrderStatus.Approved, order.Status);
        order.Dispatch(revision);
    }

    [Fact]
    public void RejectionThenEditingRequiresNewHumanApproval()
    {
        var order = Create();
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        var revision = order.Revision;
        order.Reject("supervisor", revision, Now);
        Assert.Equal(revision, order.Revision);
        order.ReplaceContent(revision, Content());
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(revision, Assert.Single(order.ApprovalHistory).Revision);
    }

    [Fact]
    public void ChangedMandatoryRequirementInvalidatesApproval()
    {
        var order = Create();
        var optional = Requirement(false);
        order.AssessSafety(order.Revision, [optional]);
        Approve(order);
        var revision = order.Revision;
        order.AssessSafety(revision, [new(optional.Id, optional.Description, true)]);
        Assert.Equal(revision + 1, order.Revision);
        Assert.Equal(WorkOrderStatus.Draft, order.Status);
        Assert.Throws<InvalidOperationException>(() => order.Dispatch(order.Revision));
        Assert.Equal(revision, Assert.Single(order.ApprovalHistory).Revision);
    }
    [Theory]
    [InlineData("ReplaceContent")]
    [InlineData("AssessSafety")]
    [InlineData("VerifyPrerequisite")]
    [InlineData("RevokeVerification")]
    [InlineData("SubmitForApproval")]
    [InlineData("Approve")]
    [InlineData("Reject")]
    [InlineData("EditAndApprove")]
    [InlineData("Dispatch")]
    public void RequirementEnumerationRejectsEveryNestedMutator(string mutation)
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        // Each attempted nested command would be valid without the guard.
        if (mutation is "Approve" or "Reject" or "EditAndApprove" or "Dispatch")
            order.SubmitForApproval(order.Revision);
        if (mutation == "Dispatch") order.Approve("supervisor", order.Revision, Now);
        var revision = order.Revision;
        var content = order.Content;
        var status = order.Status;
        var requirements = order.SafetyPrerequisites.ToArray();
        var history = order.ApprovalHistory.ToArray();
        Action nested = mutation switch
        {
            "ReplaceContent" => () => order.ReplaceContent(revision, Content()),
            "AssessSafety" => () => order.AssessSafety(revision, []),
            "VerifyPrerequisite" => () => order.VerifyPrerequisite(revision, requirement.Id, Verification(false)),
            "RevokeVerification" => () => order.RevokeVerification(revision, requirement.Id),
            "SubmitForApproval" => () => order.SubmitForApproval(revision),
            "Approve" => () => order.Approve("supervisor", revision, Now),
            "Reject" => () => order.Reject("supervisor", revision, Now),
            "EditAndApprove" => () => order.EditAndApprove(revision, Content(), [], "supervisor", Now),
            "Dispatch" => () => order.Dispatch(revision),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        IEnumerable<SafetyPrerequisite> Reenter()
        {
            nested();
            yield return requirement;
        }
        var exception = Assert.Throws<InvalidOperationException>(() =>
        {
            if (mutation == "Reject")
                order.EditAndApprove(revision, Content(), Reenter(), "supervisor", Now);
            else
                order.AssessSafety(revision, Reenter());
        });
        Assert.Equal("Work order cannot be mutated while safety requirements are being materialized.", exception.Message);
        Assert.Same(content, order.Content);
        Assert.Equal(revision, order.Revision);
        Assert.Equal(revision, order.SafetyAssessmentRevision);
        Assert.Equal(status, order.Status);
        Assert.Equal(requirements, order.SafetyPrerequisites.ToArray());
        Assert.Equal(history, order.ApprovalHistory.ToArray());
        // The guard is released, and the previously blocked operation is genuinely valid.
        nested();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaughtNestedMutationThenEnumerationFailurePreservesStateAndReleasesGuard(bool editAndApprove)
    {
        var order = Create();
        var requirement = Requirement();
        order.AssessSafety(order.Revision, [requirement]);
        order.VerifyPrerequisite(order.Revision, requirement.Id, Verification());
        order.SubmitForApproval(order.Revision);
        var revision = order.Revision;
        var content = order.Content;
        var requirements = order.SafetyPrerequisites.ToArray();
        var history = order.ApprovalHistory.ToArray();
        var failure = new InvalidOperationException("Iterator failed after rejected mutation");
        IEnumerable<SafetyPrerequisite> FailAfterReentry()
        {
            Assert.Throws<InvalidOperationException>(() => order.ReplaceContent(revision, Content()));
            // Catching the first rejection must not clear the outer materialization guard.
            Assert.Throws<InvalidOperationException>(() => order.Reject("supervisor", revision, Now));
            yield return requirement;
            throw failure;
        }
        var actual = Assert.Throws<InvalidOperationException>(() =>
        {
            if (editAndApprove)
                order.EditAndApprove(revision, Content(), FailAfterReentry(), "supervisor", Now);
            else
                order.AssessSafety(revision, FailAfterReentry());
        });
        Assert.Same(failure, actual);
        Assert.Same(content, order.Content);
        Assert.Equal(revision, order.Revision);
        Assert.Equal(revision, order.SafetyAssessmentRevision);
        Assert.Equal(WorkOrderStatus.PendingApproval, order.Status);
        Assert.Equal(requirements, order.SafetyPrerequisites.ToArray());
        Assert.Equal(history, order.ApprovalHistory.ToArray());
        order.Approve("supervisor", revision, Now);
        order.Dispatch(revision);
    }
}
