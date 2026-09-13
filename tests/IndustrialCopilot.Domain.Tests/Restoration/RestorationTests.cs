using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Domain.MaintenanceRuns;

namespace IndustrialCopilot.Domain.Tests.Restoration;

public class RestorationTests
{
    private static WorkOrderContent Content() => new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"noise","repair",[new(1,"isolate")]);
    [Fact]
    public void RestoresChangedScopeVerificationAndHistoryWithoutReplayOrSharedCollections()
    {
        var order=new WorkOrder(Guid.NewGuid(),Content());
        order.AssessSafety(1,[]); order.SubmitForApproval(2); order.Reject("supervisor",2,DateTimeOffset.UtcNow);
        order.ReplaceContent(2,Content());
        var requirement=new SafetyPrerequisite(Guid.NewGuid(),"isolation",true);
        order.AssessSafety(3,[requirement]);
        order.VerifyPrerequisite(4,requirement.Id,new("technician",DateTimeOffset.Now,"meter reading",true));
        order.SubmitForApproval(4); order.Approve("supervisor",4,DateTimeOffset.UtcNow);
        var safety=order.SafetyPrerequisites.ToArray(); var decisions=order.ApprovalHistory.ToArray();
        var restored=WorkOrder.Restore(order.Id,order.Content,4,order.Status,4,safety,decisions);
        safety[0]=new(Guid.NewGuid(),"other",false); decisions[0]=decisions[1];
        Assert.Equal(order.Content,restored.Content); Assert.Equal(4,restored.Revision);
        Assert.Equal(order.ApprovalHistory,restored.ApprovalHistory);
        Assert.Equal(order.SafetyPrerequisites[0].Verification,restored.SafetyPrerequisites[0].Verification);
        restored.Dispatch(4); Assert.Equal(WorkOrderStatus.Dispatched,restored.Status);
        Assert.Equal(WorkOrderStatus.Approved,order.Status);
    }

    [Theory]
    [InlineData(0,WorkOrderStatus.Draft,null)]
    [InlineData(1,WorkOrderStatus.Draft,1)]
    [InlineData(2,WorkOrderStatus.Draft,1)]
    [InlineData(2,WorkOrderStatus.Approved,2)]
    [InlineData(2,WorkOrderStatus.PendingApproval,null)]
    [InlineData(2,(WorkOrderStatus)99,2)]
    public void RejectsInconsistentLifecycle(int revision,WorkOrderStatus status,int? assessment) =>
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),revision,status,assessment,[],[]));

    [Fact]
    public void RejectsFutureDuplicateAndContradictoryDecisionsAndUnsafeDispatch()
    {
        var approval=ApprovalDecision.Restore("supervisor",2,ApprovalDecisionKind.Approve,DateTimeOffset.UtcNow);
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),1,WorkOrderStatus.Draft,null,[],[approval]));
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),3,WorkOrderStatus.Draft,null,[],[approval,approval]));
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),2,WorkOrderStatus.PendingApproval,2,[],[approval]));
        var p=new SafetyPrerequisite(Guid.NewGuid(),"isolate",true);
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),2,WorkOrderStatus.Dispatched,2,[p],[approval]));
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),2,WorkOrderStatus.Draft,2,[p,p],[]));
        Assert.Throws<ArgumentException>(()=>WorkOrder.Restore(Guid.NewGuid(),Content(),3,WorkOrderStatus.Draft,null,[p],[]));
        Assert.Throws<ArgumentException>(()=>ApprovalDecision.Restore("",2,ApprovalDecisionKind.Approve,DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(MaintenanceRunStatus.Running,true)]
    [InlineData(MaintenanceRunStatus.Cancelled,true)]
    [InlineData(MaintenanceRunStatus.Blocked,true)]
    [InlineData(MaintenanceRunStatus.Failed,false)]
    public void RestoresDurableRunIntent(MaintenanceRunStatus status,bool cancellation)
    {
        var run=MaintenanceRun.Restore(Guid.NewGuid(),Guid.NewGuid(),"noise",status,cancellation);
        Assert.Equal(status,run.Status); Assert.Equal(cancellation,run.IsCancellationRequested);
        Assert.Throws<InvalidOperationException>(()=>run.Start());
    }

    [Theory]
    [InlineData(MaintenanceRunStatus.Completed,true)]
    [InlineData(MaintenanceRunStatus.Cancelled,false)]
    [InlineData((MaintenanceRunStatus)99,false)]
    public void RejectsImpossibleRunState(MaintenanceRunStatus status,bool cancellation) =>
        Assert.Throws<ArgumentException>(()=>MaintenanceRun.Restore(Guid.NewGuid(),Guid.NewGuid(),"noise",status,cancellation));
}
