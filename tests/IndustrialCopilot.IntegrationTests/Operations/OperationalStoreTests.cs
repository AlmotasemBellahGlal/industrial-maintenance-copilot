using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using Npgsql;

namespace IndustrialCopilot.IntegrationTests.Operations;

// Test-only explicit allow policy: production registration never installs this.
public sealed class TestAccessPolicy : OperationalAccessPolicy
{
    public bool Denied;
    public override Task<bool> CanReadWorkOrderAsync(Guid id,string actor,CancellationToken ct)=>Task.FromResult(!Denied);
    public override Task<bool> CanSubmitAsync(SubmitWorkOrderReviewRequest request,CancellationToken ct)=>Task.FromResult(!Denied);
    public override Task<ApprovalOperationOutcome?> ValidateDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken ct)=>
        Task.FromResult<ApprovalOperationOutcome?>(Denied?ApprovalOperationOutcome.Forbidden:null);
    public override Task<bool> CanAccessTraceAsync(Guid id,bool write,CancellationToken ct)=>Task.FromResult(!Denied);
}
public class OperationalStoreTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private PostgresWorkflowStore Store=>new(database.Source);
    private static WorkOrder Order() => new(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"noise","repair",[new(1,"isolate"),new(2,"inspect")]));
    private async Task Setup()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        await new OperationalSchema(database.Source).ApplyAsync(default);
    }
    private PostgresWorkOrderApprovalService Approval(TestAccessPolicy? policy=null)=>new(Store,policy??new(),TimeProvider.System);

    [PostgresFact]
    public async Task NontrivialWorkOrderAndRunRoundTripPreservesEveryStoredField()
    {
        await Setup(); var order=Order();
        var run=new MaintenanceRun(Guid.NewGuid(),order.Content.EquipmentId,"noise"); run.Start(); run.RequestCancellation(); run.Block();
        var rt=await Store.TrySaveRunAsync(run,null,default);
        var loadedRun=(await Store.GetRunAsync(run.Id,default))!;
        Assert.Equal(MaintenanceRunStatus.Blocked,loadedRun.Run.Status); Assert.True(loadedRun.Run.IsCancellationRequested);
        Assert.Null(await Store.TrySaveRunAsync(run,"stale",default)); Assert.Equal(rt,loadedRun.ConcurrencyToken);
        order.AssessSafety(1,[]); order.SubmitForApproval(2); order.Reject("supervisor",2,DateTimeOffset.Now);
        var p=new SafetyPrerequisite(Guid.NewGuid(),"meter check",true);
        order.AssessSafety(2,[p]); order.VerifyPrerequisite(3,p.Id,new("technician",DateTimeOffset.Now,"zero volts",true));
        order.SubmitForApproval(3); order.Approve("supervisor",3,DateTimeOffset.Now);
        var token=await Store.TrySaveWorkOrderAsync(order,run.Id,null,default);
        var loaded=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Equal(order.Id,loaded.Order.Id); Assert.Equal(order.Content,loaded.Order.Content);
        Assert.Equal(3,loaded.Order.Revision); Assert.Equal(3,loaded.Order.SafetyAssessmentRevision);
        Assert.Equal(order.ApprovalHistory,loaded.Order.ApprovalHistory);
        Assert.Equal(order.SafetyPrerequisites[0].Verification,loaded.Order.SafetyPrerequisites[0].Verification);
        Assert.Equal(order.SafetyPrerequisites[0].Description,loaded.Order.SafetyPrerequisites[0].Description);
        Assert.True(loaded.Order.SafetyPrerequisites[0].IsMandatory); Assert.Equal(run.Id,loaded.MaintenanceRunId);
        loaded.Order.RevokeVerification(3,p.Id);
        var next=await Store.TrySaveWorkOrderAsync(loaded.Order,run.Id,token,default);
        Assert.NotNull(next); Assert.NotEqual(token,next); Assert.Equal(3,loaded.Order.Revision);
        Assert.Null(await Store.TrySaveWorkOrderAsync(order,run.Id,token,default));
        Assert.Throws<InvalidOperationException>(()=>loaded.Order.Dispatch(3));
    }

    [PostgresFact]
    public async Task SubmitApproveRaceAndChangedScopeCannotUseStaleApproval()
    {
        await Setup(); var order=Order(); order.AssessSafety(1,[]);
        await Store.TrySaveWorkOrderAsync(order,null,null,default);
        var service=Approval(); var draft=(await service.GetReviewAsync(order.Id,"user",default))!;
        var submitted=await service.SubmitForReviewAsync(new(draft.Target,"user"),default);
        Assert.Equal(ApprovalOperationOutcome.Applied,submitted.Outcome);
        Assert.Equal(draft.Target.Revision,submitted.Snapshot!.Target.Revision);
        Assert.NotEqual(draft.Target.ConcurrencyToken,submitted.Snapshot.Target.ConcurrencyToken);
        Assert.Equal(ApprovalOperationOutcome.Conflict,(await service.SubmitForReviewAsync(new(draft.Target,"user"),default)).Outcome);
        var request=RecordWorkOrderDecisionRequest.Approve(submitted.Snapshot.Target,"supervisor");
        var results=await Task.WhenAll(service.RecordDecisionAsync(request,default),service.RecordDecisionAsync(request,default));
        Assert.Single(results,x=>x.Outcome==ApprovalOperationOutcome.Applied);
        Assert.Single(results,x=>x.Outcome==ApprovalOperationOutcome.Conflict);
        var current=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Single(current.Order.ApprovalHistory);
        current.Order.ReplaceContent(current.Order.Revision,current.Order.Content);
        await Store.TrySaveWorkOrderAsync(current.Order,null,current.ConcurrencyToken,default);
        var changed=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Throws<InvalidOperationException>(()=>changed.Order.Dispatch(changed.Order.Revision));
        Assert.Equal(ApprovalOperationOutcome.Conflict,(await service.RecordDecisionAsync(request,default)).Outcome);
    }

    [PostgresFact]
    public async Task EditAndApproveInstallsExactScopeClearsVerificationAndRejectPersists()
    {
        await Setup(); var order=Order(); var p=new SafetyPrerequisite(Guid.NewGuid(),"old",true);
        order.AssessSafety(1,[p]); order.VerifyPrerequisite(2,p.Id,new("tech",DateTimeOffset.Now,"meter",true)); order.SubmitForApproval(2);
        await Store.TrySaveWorkOrderAsync(order,null,null,default);
        var service=Approval(); var target=(await service.GetReviewAsync(order.Id,"supervisor",default))!.Target;
        var scope=new ReviewedWorkOrderScope(new(order.Content.EquipmentId,order.Content.ManualId,order.Content.ManualRevisionId,"new symptom","final scope",[new(3,"final action")]),
            [new(Guid.NewGuid(),"new requirement",false)]);
        var applied=await service.RecordDecisionAsync(RecordWorkOrderDecisionRequest.EditAndApprove(target,"supervisor",scope),default);
        Assert.Equal(ApprovalOperationOutcome.Applied,applied.Outcome);
        var current=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Equal(3,current.Order.Revision); Assert.Equal(scope.Content,current.Order.Content);
        Assert.Equal(scope.SafetyPrerequisites[0].Id,Assert.Single(current.Order.SafetyPrerequisites).Id);
        Assert.Null(current.Order.SafetyPrerequisites[0].Verification);
        Assert.Equal(ApprovalDecisionKind.EditAndApprove,Assert.Single(current.Order.ApprovalHistory).Kind);
        await using(var cmd=database.Source.CreateCommand("SELECT description FROM operations.work_order_snapshots WHERE id=@id AND token=@token"))
        {
            cmd.Parameters.AddWithValue("id",order.Id); cmd.Parameters.AddWithValue("token",Guid.Parse(target.ConcurrencyToken));
            Assert.Equal(order.Content.Description,await cmd.ExecuteScalarAsync());
        }
        current.Order.AssessSafety(3,[]); current.Order.SubmitForApproval(4);
        await Store.TrySaveWorkOrderAsync(current.Order,null,current.ConcurrencyToken,default);
        target=(await service.GetReviewAsync(order.Id,"supervisor",default))!.Target;
        Assert.Equal(ApprovalOperationOutcome.Applied,(await service.RecordDecisionAsync(RecordWorkOrderDecisionRequest.Reject(target,"supervisor"),default)).Outcome);
        var rejected=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Equal(WorkOrderStatus.Rejected,rejected.Order.Status);
        Assert.Throws<InvalidOperationException>(()=>rejected.Order.Dispatch(4));
    }

    [PostgresFact]
    public async Task FailedEditedScopeRollsBackHeadChildrenAndHistory()
    {
        await Setup(); var order=Order(); order.AssessSafety(1,[]); order.SubmitForApproval(2);
        var token=await Store.TrySaveWorkOrderAsync(order,null,null,default);
        var scope=new ReviewedWorkOrderScope(new(order.Content.EquipmentId,order.Content.ManualId,order.Content.ManualRevisionId,"noise","edited scope",[new(1,"first"),new(2,"bad\0instruction")]),[]);
        await Assert.ThrowsAsync<OperationalStoreException>(()=>Approval().RecordDecisionAsync(RecordWorkOrderDecisionRequest.EditAndApprove(new(order.Id,2,token!),"supervisor",scope),default));
        var loaded=(await Store.GetWorkOrderAsync(order.Id,default))!;
        Assert.Equal(token,loaded.ConcurrencyToken); Assert.Equal(order.Content,loaded.Order.Content);
        Assert.Equal(2,loaded.Order.Revision); Assert.Empty(loaded.Order.ApprovalHistory);
        Assert.Equal(WorkOrderStatus.PendingApproval,loaded.Order.Status);
    }

    [PostgresFact]
    public async Task CancellationWaitingOnHeadLockDoesNotPersistApproval()
    {
        await Setup(); var order=Order(); order.AssessSafety(1,[]); order.SubmitForApproval(2);
        var token=await Store.TrySaveWorkOrderAsync(order,null,null,default);
        await using var c=await database.Source.OpenConnectionAsync(); await using var t=await c.BeginTransactionAsync();
        await using var cmd=new NpgsqlCommand("SELECT 1 FROM operations.work_orders WHERE id=@id FOR UPDATE",c,t);
        cmd.Parameters.AddWithValue("id",order.Id); await cmd.ExecuteNonQueryAsync();
        using var cancellation=new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Approval().RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(new(order.Id,2,token!),"supervisor"),cancellation.Token));
        await t.RollbackAsync();
        Assert.Equal(token,(await Store.GetWorkOrderAsync(order.Id,default))!.ConcurrencyToken);
    }

    [PostgresFact]
    public async Task PolicyDenialsAndInvalidLifecycleHaveNoSideEffects()
    {
        await Setup(); var order=Order(); var token=await Store.TrySaveWorkOrderAsync(order,null,null,default);
        var target=new WorkOrderReviewTarget(order.Id,1,token!); var denied=Approval(new(){Denied=true});
        Assert.Null(await denied.GetReviewAsync(order.Id,"actor",default));
        Assert.Equal(ApprovalOperationOutcome.Forbidden,(await denied.RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(target,"actor"),default)).Outcome);
        Assert.Equal(ApprovalOperationOutcome.SafetyValidationFailed,(await Approval().SubmitForReviewAsync(new(target,"actor"),default)).Outcome);
        Assert.Equal(ApprovalOperationOutcome.InvalidState,(await Approval().RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(target,"actor"),default)).Outcome);
        Assert.Equal(token,(await Store.GetWorkOrderAsync(order.Id,default))!.ConcurrencyToken);
    }

    [PostgresFact]
    public async Task TraceConcurrentWritersEquivalentRetriesAndLostObservations()
    {
        await Setup(); var store=new PostgresRunTraceStore(database.Source,new TestAccessPolicy());
        var id=Guid.NewGuid(); var correlation=Guid.NewGuid(); var start=DateTimeOffset.Now;
        var step=new TraceStep(Guid.NewGuid(),null,TraceOperationKind.Llm,"completion",start,TraceStepStatus.Running);
        var initial=new RunTraceSnapshot(id,correlation,null,1,[step]);
        Assert.All(await Task.WhenAll(store.TrySaveAsync(initial,0,default),store.TrySaveAsync(initial,0,default)),Assert.True);
        Assert.Equal(step,(await store.GetAsync(id,default))!.Root);
        var first=new RunTraceSnapshot(id,correlation,null,2,[new(step.StepId,null,step.Kind,step.OperationName,start,TraceStepStatus.Completed,start.AddSeconds(1))]);
        var second=new RunTraceSnapshot(id,correlation,null,2,[new(step.StepId,null,step.Kind,step.OperationName,start,TraceStepStatus.Cancelled,start.AddSeconds(1))]);
        var results=await Task.WhenAll(store.TrySaveAsync(first,1,default),store.TrySaveAsync(second,1,default));
        Assert.Single(results,x=>x); Assert.Single(results,x=>!x);
        var current=(await store.GetAsync(id,default))!;
        Assert.True(await store.TrySaveAsync(current,1,default));
        var bad=new RunTraceSnapshot(id,correlation,null,3,[step]);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.TrySaveAsync(bad,2,default));
        Assert.Equal(2,(await store.GetAsync(id,default))!.Version);
        using var cancelled=new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>store.GetAsync(id,cancelled.Token));
    }

    [PostgresFact]
    public async Task InvalidStoredLifecycleIsRejectedDuringRehydration()
    {
        await Setup(); var order=Order(); order.AssessSafety(1,[]);
        var token=await Store.TrySaveWorkOrderAsync(order,null,null,default);
        await using var cmd=database.Source.CreateCommand("UPDATE operations.work_order_snapshots SET status=3 WHERE id=@id AND token=@token");
        cmd.Parameters.AddWithValue("id",order.Id); cmd.Parameters.AddWithValue("token",Guid.Parse(token!));
        await cmd.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<ArgumentException>(()=>Store.GetWorkOrderAsync(order.Id,default));
    }

    [PostgresFact]
    public async Task TraceTreeErrorsReferencesAndLateCostsRoundTripWithoutDoubleAccounting()
    {
        await Setup(); var store=new PostgresRunTraceStore(database.Source,new TestAccessPolicy());
        var execution=Guid.NewGuid(); var correlation=Guid.NewGuid(); var run=Guid.NewGuid(); var work=Guid.NewGuid();
        var at=DateTimeOffset.Now; var root=Guid.NewGuid(); var call=Guid.NewGuid();
        TraceStep[] Steps(bool priced) => [
            new(root,null,TraceOperationKind.Orchestration,"workflow",at,TraceStepStatus.Running),
            new(call,root,TraceOperationKind.Llm,"completion",at,TraceStepStatus.Failed,at.AddSeconds(1),new("provider_unavailable","Provider unavailable"),
                usage:priced?new(2,3,5):null,cost:priced?new(1.25m,"USD"):null),
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"),root,TraceOperationKind.Llm,"embedding",at,TraceStepStatus.Completed,at.AddSeconds(1),usage:new(4,0,4),cost:new(2m,"EUR")),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"),root,TraceOperationKind.Approval,"human_review",at,TraceStepStatus.Waiting,workOrderId:work,workOrderRevision:3),
            new(Guid.Parse("00000000-0000-0000-0000-000000000003"),root,TraceOperationKind.Tool,"manual_lookup",at,TraceStepStatus.Completed,at.AddSeconds(1))];
        Assert.True(await store.TrySaveAsync(new(execution,correlation,null,1,Steps(false)),0,default));
        var final=new RunTraceSnapshot(execution,correlation,run,2,Steps(true));
        Assert.True(await store.TrySaveAsync(final,1,default));
        Assert.True(await store.TrySaveAsync(new(execution,correlation,run,2,Steps(true).Reverse().ToArray()),1,default));
        var loaded=(await store.GetAsync(execution,default))!;
        Assert.Equal(final.Steps.OrderBy(s=>s.StepId),loaded.Steps.OrderBy(s=>s.StepId));
        Assert.Equal(9,loaded.Usage.KnownTokenUsage.TotalTokens);
        Assert.Equal(new[]{"EUR","USD"},loaded.Usage.KnownCostsByCurrency.Select(c=>c.Currency));
        Assert.Equal(run,loaded.MaintenanceRunId);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.TrySaveAsync(new(execution,correlation,run,3,Steps(false)),2,default));
    }
}
