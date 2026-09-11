using IndustrialCopilot.Application.Abstractions.Approval;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Tests.Abstractions.Approval;

public class ApprovalContractTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
    private static WorkOrderContent Content() => new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
        "Vibration", "Replace coupling", [new(1, "Remove coupling"), new(3, "Fit replacement")]);
    private static WorkOrderReviewTarget Target(int revision = 2) => new(Guid.NewGuid(), revision, "opaque-token");
    private static SafetyPrerequisite Requirement() => new(Guid.NewGuid(), "Isolate power", true);
    private static WorkOrder Pending(params SafetyPrerequisite[] requirements)
    {
        var order = new WorkOrder(Guid.NewGuid(), Content());
        order.AssessSafety(order.Revision, requirements);
        order.SubmitForApproval(order.Revision);
        return order;
    }
    private static WorkOrderReviewSnapshot Snapshot(WorkOrder order) => new(
        new(order.Id, order.Revision, "opaque-token"), order.Content, order.Status,
        order.SafetyAssessmentRevision, order.SafetyPrerequisites, order.ApprovalHistory.LastOrDefault());

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TargetRejectsNonpositiveRevision(int revision) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Target(revision));

    [Fact]
    public void TargetRejectsEmptyIdentityAndKeepsConcurrencyIndependentOfRevision()
    {
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewTarget(Guid.Empty, 1, "token"));
        var first = Target();
        var changed = new WorkOrderReviewTarget(first.WorkOrderId, first.Revision, "another-token");
        Assert.NotEqual(first, changed);
        Assert.Equal(first.Revision, changed.Revision);
        Assert.Equal("another-token", changed.ConcurrencyToken);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RequiredTokensAndActorIdentitiesRejectBlankValues(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderReviewTarget(Guid.NewGuid(), 1, text!));
        Assert.ThrowsAny<ArgumentException>(() => new SubmitWorkOrderReviewRequest(Target(), text!));
        Assert.ThrowsAny<ArgumentException>(() => RecordWorkOrderDecisionRequest.Approve(Target(), text!));
        Assert.ThrowsAny<ArgumentException>(() => RecordWorkOrderDecisionRequest.Reject(Target(), text!));
        Assert.ThrowsAny<ArgumentException>(() => RecordWorkOrderDecisionRequest.EditAndApprove(Target(), text!, new(Content(), [])));
    }

    [Fact]
    public void RequestsRequireTargetAndOnlyEditIntentCarriesFinalScope()
    {
        var scope = new ReviewedWorkOrderScope(Content(), []);
        Assert.Throws<ArgumentNullException>(() => new SubmitWorkOrderReviewRequest(null!, "actor"));
        Assert.Throws<ArgumentNullException>(() => RecordWorkOrderDecisionRequest.Approve(null!, "actor"));
        Assert.Throws<ArgumentNullException>(() => RecordWorkOrderDecisionRequest.Reject(null!, "actor"));
        Assert.Throws<ArgumentNullException>(() => RecordWorkOrderDecisionRequest.EditAndApprove(null!, "actor", scope));
        Assert.Throws<ArgumentNullException>(() => RecordWorkOrderDecisionRequest.EditAndApprove(Target(), "actor", null!));
        var approve = RecordWorkOrderDecisionRequest.Approve(Target(), "actor");
        var reject = RecordWorkOrderDecisionRequest.Reject(Target(), "actor");
        var edit = RecordWorkOrderDecisionRequest.EditAndApprove(Target(), "actor", scope);
        Assert.Equal(ApprovalDecisionKind.Approve, approve.Kind);
        Assert.Equal(ApprovalDecisionKind.Reject, reject.Kind);
        Assert.Null(approve.EditedScope);
        Assert.Null(reject.EditedScope);
        Assert.Equal(ApprovalDecisionKind.EditAndApprove, edit.Kind);
        Assert.Same(scope, edit.EditedScope);
    }

    [Fact]
    public void FinalScopeRequiresContentAndExplicitUniqueRequirementSet()
    {
        var requirement = Requirement();
        Assert.Throws<ArgumentNullException>(() => new ReviewedWorkOrderScope(null!, []));
        Assert.Throws<ArgumentNullException>(() => new ReviewedWorkOrderScope(Content(), null!));
        Assert.Throws<ArgumentException>(() => new ReviewedWorkOrderScope(Content(), [null!]));
        Assert.Throws<ArgumentException>(() => new ReviewedWorkOrderScope(Content(),
            [requirement, new(requirement.Id, "Other requirement", false)]));
        var explicitEmpty = new ReviewedWorkOrderScope(Content(), []);
        Assert.Empty(explicitEmpty.SafetyPrerequisites);
    }

    [Fact]
    public void FinalScopeOwnsDefinitionsAndCannotImportVerification()
    {
        var requirement = Requirement();
        var supplied = new List<SafetyPrerequisite> { requirement };
        var scope = new ReviewedWorkOrderScope(Content(), supplied);
        supplied.Clear();
        Assert.Same(requirement, Assert.Single(scope.SafetyPrerequisites));
        Assert.Throws<NotSupportedException>(() => ((IList<SafetyPrerequisite>)scope.SafetyPrerequisites).Clear());
        var order = Pending(requirement);
        order.VerifyPrerequisite(order.Revision, requirement.Id, new("technician", Now, "Isolation checked", true));
        Assert.Throws<ArgumentException>(() => new ReviewedWorkOrderScope(Content(), order.SafetyPrerequisites));
    }

    [Fact]
    public void SnapshotRejectsInvalidInputsAndAbsentOrStaleAssessment()
    {
        var target = Target();
        var content = Content();
        Assert.Throws<ArgumentNullException>(() => new WorkOrderReviewSnapshot(null!, content, WorkOrderStatus.Draft, null, []));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderReviewSnapshot(target, null!, WorkOrderStatus.Draft, null, []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkOrderReviewSnapshot(target, content, (WorkOrderStatus)999, null, []));
        Assert.Throws<ArgumentNullException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, null, null!));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, null, [Requirement()]));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.PendingApproval, null, []));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, 1, []));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, 3, []));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, 2, [null!]));
        var requirement = Requirement();
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, content, WorkOrderStatus.Draft, 2, [requirement, requirement]));
    }

    [Fact]
    public void SnapshotRejectsContradictoryDecisionRevisionAndStatus()
    {
        var order = Pending();
        order.Reject("supervisor", order.Revision, Now);
        var rejected = order.ApprovalHistory.Single();
        var target = new WorkOrderReviewTarget(order.Id, order.Revision, "token");
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, order.Content, WorkOrderStatus.Approved, order.Revision, [], rejected));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, order.Content, WorkOrderStatus.PendingApproval, order.Revision, [], rejected));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(target, order.Content, WorkOrderStatus.Rejected, order.Revision, []));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(new(order.Id, 1, "token"), order.Content, WorkOrderStatus.Draft, null, [], rejected));
        order.ReplaceContent(order.Revision, Content());
        Assert.Equal(rejected, Snapshot(order).LatestDecision); // Valid older history on a revised draft.
        order.AssessSafety(order.Revision, []);
        order.SubmitForApproval(order.Revision);
        order.Approve("supervisor", order.Revision, Now);
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(new(order.Id, order.Revision, "token"), order.Content,
            WorkOrderStatus.Rejected, order.Revision, [], order.ApprovalHistory.Last()));
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(new(order.Id, order.Revision, "token"), order.Content,
            WorkOrderStatus.Approved, order.Revision, [], rejected));
    }

    [Fact]
    public void SnapshotPreservesVerificationReadStateWithoutEquatingApprovalToSafety()
    {
        var requirement = Requirement();
        var order = Pending(requirement);
        order.Approve("supervisor", order.Revision, Now);
        var supplied = order.SafetyPrerequisites.ToList();
        var before = new WorkOrderReviewSnapshot(new(order.Id, order.Revision, "token"), order.Content,
            order.Status, order.SafetyAssessmentRevision, supplied, order.ApprovalHistory.Single());
        supplied.Clear();
        Assert.Equal(SafetyPrerequisiteStatus.Unverified, Assert.Single(before.SafetyPrerequisites).Status);
        Assert.Throws<NotSupportedException>(() => ((IList<SafetyPrerequisite>)before.SafetyPrerequisites).Clear());
        Assert.Throws<ArgumentException>(() => new WorkOrderReviewSnapshot(before.Target, before.Content,
            WorkOrderStatus.Dispatched, before.SafetyAssessmentRevision, before.SafetyPrerequisites, before.LatestDecision));
        order.VerifyPrerequisite(order.Revision, requirement.Id, new("technician", Now, "Checked", true));
        order.Dispatch(order.Revision);
        Assert.Equal(WorkOrderStatus.Dispatched, Snapshot(order).Status);
        Assert.Equal(SafetyPrerequisiteStatus.Unverified, before.SafetyPrerequisites.Single().Status);
    }

    [Fact]
    public void OperationResultsRequireConsistentPayloads()
    {
        var pending = Snapshot(Pending());
        Assert.Throws<ArgumentNullException>(() => ApprovalOperationResult.Applied(null!));
        Assert.Throws<ArgumentException>(() => ApprovalOperationResult.Applied(Snapshot(new WorkOrder(Guid.NewGuid(), Content()))));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApprovalOperationResult.Failed(ApprovalOperationOutcome.Applied, "reason"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ApprovalOperationResult.Failed((ApprovalOperationOutcome)999, "reason"));
        Assert.Throws<ArgumentException>(() => ApprovalOperationResult.Failed(ApprovalOperationOutcome.NotFound, "Missing", pending));
        Assert.Throws<ArgumentException>(() => ApprovalOperationResult.Failed(ApprovalOperationOutcome.Forbidden, "Denied", pending));
        var applied = ApprovalOperationResult.Applied(pending);
        Assert.Same(pending, applied.Snapshot);
        Assert.Null(applied.Explanation);
        Assert.Equal(ApprovalOperationOutcome.Applied, applied.Outcome);
    }

    [Theory]
    [InlineData(ApprovalOperationOutcome.NotFound)]
    [InlineData(ApprovalOperationOutcome.Conflict)]
    [InlineData(ApprovalOperationOutcome.InvalidState)]
    [InlineData(ApprovalOperationOutcome.SafetyValidationFailed)]
    [InlineData(ApprovalOperationOutcome.InvalidEdit)]
    [InlineData(ApprovalOperationOutcome.Forbidden)]
    public void ExpectedFailuresRequireExplanationAndDoNotClaimApplied(ApprovalOperationOutcome outcome)
    {
        Assert.Throws<ArgumentException>(() => ApprovalOperationResult.Failed(outcome, " "));
        var result = ApprovalOperationResult.Failed(outcome, "Reason");
        Assert.Equal(outcome, result.Outcome);
        Assert.Null(result.Snapshot);
        Assert.Equal("Reason", result.Explanation);
    }

    [Fact]
    public void ApprovalPortIsAsyncCancellableAndUsesOnlyNeutralBoundaryTypes()
    {
        // This reflection check protects the port's async and dependency boundary, not DTO getters.
        foreach (var method in typeof(IWorkOrderApprovalService).GetMethods())
        {
            Assert.Equal(typeof(CancellationToken), method.GetParameters().Last().ParameterType);
            Assert.Equal(typeof(Task<>), method.ReturnType.GetGenericTypeDefinition());
            foreach (var type in method.GetParameters().Select(p => p.ParameterType).Append(method.ReturnType.GenericTypeArguments.Single()))
                Assert.True(type.Assembly == typeof(string).Assembly || type.Assembly == typeof(WorkOrderReviewTarget).Assembly);
        }
        var references = typeof(IWorkOrderApprovalService).Assembly.GetReferencedAssemblies();
        Assert.DoesNotContain(references, reference => reference.Name!.StartsWith("IndustrialCopilot.") && reference.Name != "IndustrialCopilot.Domain");
        Assert.DoesNotContain(references, reference => reference.Name!.Contains("EntityFramework") || reference.Name.Contains("AspNetCore") || reference.Name.Contains("OpenAI"));
    }
}
