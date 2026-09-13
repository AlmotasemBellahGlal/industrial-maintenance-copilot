using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;

namespace IndustrialCopilot.IntegrationTests.Operations;

public class DispatchTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private readonly Auth auth=new();
    private PostgresWorkflowStore Store=>new(database.Source);
    private PostgresDispatchAttemptStore Attempts=>new(database.Source,auth);
    private sealed class Auth : IActionAuthorization
    {
        public bool Denied;
        public Task<bool> AuthorizeAsync(string actor,TrustedAction action,Guid id,CancellationToken ct)=>Task.FromResult(!Denied && actor=="trusted-host");
    }
    private sealed class Receiver : IExternalDispatch
    {
        public int Sends; public int Effects; public bool Accepted; public bool Reject; public bool Timeout;
        public Action? AfterSend;
        public readonly List<Guid> Keys=[];
        public Task<ExternalDispatchResult> SendAsync(DispatchAttempt attempt,CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); Sends++; Keys.Add(attempt.Id);
            if(Reject) return Task.FromResult(ExternalDispatchResult.Failed());
            if(!Accepted) { Accepted=true; Effects++; }
            AfterSend?.Invoke(); ct.ThrowIfCancellationRequested();
            if(Timeout) throw new TimeoutException("not for audit");
            return Task.FromResult(ExternalDispatchResult.Accepted("ticket"));
        }
        public Task<ExternalDispatchResult> ReconcileAsync(Guid key,CancellationToken ct)
        { Keys.Add(key); return Task.FromResult(Accepted?ExternalDispatchResult.Accepted("ticket"):Reject?ExternalDispatchResult.Failed():ExternalDispatchResult.Uncertain()); }
    }
    private async Task<(DispatchCommand Command,Guid Run,Guid Requirement)> Seed(bool verified=true,bool approve=true)
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var order=new WorkOrder(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"noise","inspect pump",[new(1,"isolate and inspect")]));
        var requirement=new SafetyPrerequisite(Guid.NewGuid(),"isolation",true);
        order.AssessSafety(1,[requirement]); order.SubmitForApproval(2);
        var run=new MaintenanceRun(Guid.NewGuid(),order.Content.EquipmentId,"noise"); run.Start(); run.WaitForApproval();
        await Store.TrySaveRunAsync(run,null,default);
        var token=(await Store.TrySaveWorkOrderAsync(order,run.Id,null,default))!;
        var target=new WorkOrderReviewTarget(order.Id,2,token);
        if(approve)
        {
            var service=new AuthorizedApprovalService(new PostgresWorkOrderApprovalService(Store,new TestAccessPolicy(),TimeProvider.System),auth);
            var result=await service.RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(target,"trusted-host"),default);
            Assert.Equal(ApprovalOperationOutcome.Applied,result.Outcome); target=result.Snapshot!.Target;
        }
        if(verified)
        {
            var result=await new PostgresTrustedContext(database.Source,auth).RecordAsync(target,requirement.Id,"trusted-host","meter observed zero",true,default);
            Assert.Equal(DispatchGateOutcome.Ready,result.Outcome); target=new(order.Id,2,result.ConcurrencyToken!);
        }
        return(new(target,"trusted-host"),run.Id,requirement.Id);
    }

    [PostgresFact]
    public async Task AcceptedDeliveryAtomicallyConfirmsOrderRunAndAuditAndDuplicateConverges()
    {
        var s=await Seed(); var receiver=new Receiver(); var coordinator=new DispatchCoordinator(Attempts,receiver,auth);
        var results=await Task.WhenAll(coordinator.DispatchAsync(s.Command,default),coordinator.DispatchAsync(s.Command,default));
        Assert.All(results,r=>Assert.Equal(DispatchAttemptState.Confirmed,r.Attempt!.State));
        Assert.Single(results.Select(x=>x.Attempt!.Id).Distinct()); Assert.Equal(1,receiver.Sends); Assert.Equal(1,receiver.Effects);
        Assert.Equal(WorkOrderStatus.Dispatched,(await Store.GetWorkOrderAsync(s.Command.Target.WorkOrderId,default))!.Order.Status);
        Assert.Equal(MaintenanceRunStatus.Completed,(await Store.GetRunAsync(s.Run,default))!.Run.Status);
        await using var audit=database.Source.CreateCommand("SELECT count(*) FROM operations.dispatch_events WHERE attempt_id=@id");
        audit.Parameters.AddWithValue("id",results[0].Attempt!.Id); Assert.Equal(3L,await audit.ExecuteScalarAsync());
        await coordinator.RetryAsync(results[0].Attempt!.Id,"trusted-host",default); Assert.Equal(1,receiver.Sends);
    }

    private sealed class FailConfirmation(IDispatchAttemptStore inner) : IDispatchAttemptStore
    {
        public Task<DispatchReservation> ReserveAsync(DispatchCommand c,CancellationToken ct)=>inner.ReserveAsync(c,ct);
        public Task<DispatchAttempt?> GetAsync(Guid id,CancellationToken ct)=>inner.GetAsync(id,ct);
        public async Task<DispatchAttemptSession?> OpenAsync(Guid id,CancellationToken ct)=>new Failing((await inner.OpenAsync(id,ct))!);
        private sealed class Failing(DispatchAttemptSession inner) : DispatchAttemptSession
        {
            public override DispatchAttempt Attempt=>inner.Attempt;
            public override Task MarkInvocationStartedAsync(CancellationToken ct)=>inner.MarkInvocationStartedAsync(ct);
            public override Task<bool> RestartAsync(string actor,CancellationToken ct)=>inner.RestartAsync(actor,ct);
            public override Task RecordAsync(ExternalDispatchResult result,string actor,CancellationToken ct)=>throw new IOException("injected confirmation outage");
            public override ValueTask DisposeAsync()=>inner.DisposeAsync();
        }
    }
    [PostgresFact]
    public async Task AcceptedThenConfirmationOutageReconcilesSameKeyWithoutResending()
    {
        var s=await Seed(); var receiver=new Receiver();
        await Assert.ThrowsAsync<IOException>(()=>new DispatchCoordinator(new FailConfirmation(Attempts),receiver,auth).DispatchAsync(s.Command,default));
        var id=Assert.Single(receiver.Keys); var pending=(await Attempts.GetAsync(id,default))!;
        Assert.Equal(DispatchAttemptState.Pending,pending.State); Assert.True(pending.InvocationStarted);
        Assert.Equal(WorkOrderStatus.Approved,(await Store.GetWorkOrderAsync(pending.WorkOrderId,default))!.Order.Status);
        var coordinator=new DispatchCoordinator(Attempts,receiver,auth);
        await coordinator.DispatchAsync(s.Command,default); Assert.Equal(1,receiver.Sends);
        var confirmed=await coordinator.ReconcileAsync(id,"trusted-host",default);
        Assert.Equal(DispatchAttemptState.Confirmed,confirmed.Attempt!.State); Assert.Equal(1,receiver.Effects);
        Assert.All(receiver.Keys,key=>Assert.Equal(id,key));
    }
    [PostgresFact]
    public async Task TimeoutAfterAcceptanceFreezesUntilReconciliationConfirms()
    {
        var s=await Seed(); var receiver=new Receiver {Timeout=true}; var coordinator=new DispatchCoordinator(Attempts,receiver,auth);
        var result=await coordinator.DispatchAsync(s.Command,default);
        Assert.Equal(DispatchAttemptState.Uncertain,result.Attempt!.State);
        await AssertFrozen(s.Command.Target.WorkOrderId,s.Run,s.Requirement);
        Assert.Equal(DispatchAttemptState.Confirmed,(await coordinator.ReconcileAsync(result.Attempt.Id,"trusted-host",default)).Attempt!.State);
        Assert.Equal(1,receiver.Sends); Assert.Equal(1,receiver.Effects);
    }
    [PostgresFact]
    public async Task PendingFreezesEditsApprovalAssessmentsVerificationsAndRunCancellation()
    {
        var s=await Seed(); Assert.Equal(DispatchAttemptState.Pending,(await Attempts.ReserveAsync(s.Command,default)).Attempt!.State);
        await AssertFrozen(s.Command.Target.WorkOrderId,s.Run,s.Requirement);
    }
    private async Task AssertFrozen(Guid id,Guid runId,Guid requirement)
    {
        foreach(var mutation in new Action<WorkOrder>[] {o=>o.ReplaceContent(o.Revision,o.Content),o=>o.AssessSafety(o.Revision,[]),o=>o.RevokeVerification(o.Revision,requirement)})
        {
            var stored=(await Store.GetWorkOrderAsync(id,default))!; mutation(stored.Order);
            Assert.Null(await Store.TrySaveWorkOrderAsync(stored.Order,runId,stored.ConcurrencyToken,default));
        }
        var current=(await Store.GetWorkOrderAsync(id,default))!;
        var target=new WorkOrderReviewTarget(id,current.Order.Revision,current.ConcurrencyToken);
        var approval=new PostgresWorkOrderApprovalService(Store,new TestAccessPolicy(),TimeProvider.System);
        Assert.Equal(ApprovalOperationOutcome.Conflict,(await approval.RecordDecisionAsync(RecordWorkOrderDecisionRequest.EditAndApprove(target,"trusted-host",new(current.Order.Content,[])),default)).Outcome);
        Assert.Equal(DispatchGateOutcome.Conflict,(await new PostgresTrustedContext(database.Source,auth).RecordAsync(target,requirement,"trusted-host","revoked",false,default)).Outcome);
        var run=(await Store.GetRunAsync(runId,default))!; run.Run.RequestCancellation();
        Assert.Null(await Store.TrySaveRunAsync(run.Run,run.ConcurrencyToken,default));
        Assert.False((await Store.GetRunAsync(runId,default))!.Run.IsCancellationRequested);
        Assert.True((await Store.GetWorkOrderAsync(id,default))!.Order.SafetyPrerequisites[0].Verification!.IsSatisfied);
    }
    [PostgresFact]
    public async Task ReconciledNonAcceptanceCanRetryOnlyUnchangedScopeWithSameKey()
    {
        var s=await Seed(); var receiver=new Receiver {Reject=true}; var coordinator=new DispatchCoordinator(Attempts,receiver,auth);
        var reserved=(await Attempts.ReserveAsync(s.Command,default)).Attempt!;
        var failed=await coordinator.ReconcileAsync(reserved.Id,"trusted-host",default);
        Assert.Equal(DispatchAttemptState.DefinitivelyFailed,failed.Attempt!.State);
        receiver.Reject=false;
        Assert.Equal(DispatchAttemptState.Confirmed,(await coordinator.RetryAsync(reserved.Id,"trusted-host",default)).Attempt!.State);
        Assert.All(receiver.Keys,k=>Assert.Equal(reserved.Id,k)); Assert.Equal(1,receiver.Effects);
        var other=await Seed(); receiver=new() {Reject=true}; coordinator=new(Attempts,receiver,auth);
        failed=await coordinator.DispatchAsync(other.Command,default);
        var current=(await Store.GetWorkOrderAsync(other.Command.Target.WorkOrderId,default))!;
        current.Order.ReplaceContent(current.Order.Revision,current.Order.Content);
        Assert.NotNull(await Store.TrySaveWorkOrderAsync(current.Order,other.Run,current.ConcurrencyToken,default));
        Assert.Equal(DispatchGateOutcome.Conflict,(await coordinator.RetryAsync(failed.Attempt!.Id,"trusted-host",default)).Outcome);
        Assert.Equal(1,receiver.Sends);
    }
    [PostgresFact]
    public async Task MissingApprovalVerificationStaleScopeAndDeniedIdentityFailClosed()
    {
        var missing=await Seed(approve:false); Assert.Equal(DispatchGateOutcome.NotDispatchable,(await Attempts.ReserveAsync(missing.Command,default)).Outcome);
        var unverified=await Seed(verified:false); Assert.Equal(DispatchGateOutcome.NotDispatchable,(await Attempts.ReserveAsync(unverified.Command,default)).Outcome);
        var valid=await Seed();
        Assert.Equal(DispatchGateOutcome.Conflict,(await Attempts.ReserveAsync(new(new(valid.Command.Target.WorkOrderId,1,valid.Command.Target.ConcurrencyToken),"trusted-host"),default)).Outcome);
        Assert.Equal(DispatchGateOutcome.Forbidden,(await Attempts.ReserveAsync(new(valid.Command.Target,"supervisor"),default)).Outcome);
        auth.Denied=true; Assert.Equal(DispatchGateOutcome.Forbidden,(await Attempts.ReserveAsync(valid.Command,default)).Outcome);
    }
    [PostgresFact]
    public async Task CancelledBlockedAndRejectedCannotReserve()
    {
        foreach(var state in new[]{"cancel","block","reject"})
        {
            var s=await Seed(approve:state!="reject");
            if(state=="reject")
            {
                var approval=new PostgresWorkOrderApprovalService(Store,new TestAccessPolicy(),TimeProvider.System);
                var result=await approval.RecordDecisionAsync(RecordWorkOrderDecisionRequest.Reject(s.Command.Target,"trusted-host"),default);
                s=(new(result.Snapshot!.Target,"trusted-host"),s.Run,s.Requirement);
            }
            else
            {
                var stored=(await Store.GetRunAsync(s.Run,default))!;
                if(state=="cancel") stored.Run.RequestCancellation(); else {stored.Run.Resume(); stored.Run.Block();}
                await Store.TrySaveRunAsync(stored.Run,stored.ConcurrencyToken,default);
            }
            Assert.Equal(DispatchGateOutcome.NotDispatchable,(await Attempts.ReserveAsync(s.Command,default)).Outcome);
        }
    }
    [PostgresFact]
    public async Task CancellationBeforeInvocationHasNoEffectAndAfterSendRemainsUncertain()
    {
        var s=await Seed(); var receiver=new Receiver(); var coordinator=new DispatchCoordinator(Attempts,receiver,auth);
        using var before=new CancellationTokenSource(); before.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>coordinator.DispatchAsync(s.Command,before.Token)); Assert.Equal(0,receiver.Sends);
        using var after=new CancellationTokenSource(); receiver.AfterSend=after.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>coordinator.DispatchAsync(s.Command,after.Token));
        var id=Assert.Single(receiver.Keys); Assert.Equal(DispatchAttemptState.Uncertain,(await Attempts.GetAsync(id,default))!.State);
        Assert.Equal(DispatchAttemptState.Confirmed,(await coordinator.ReconcileAsync(id,"trusted-host",default)).Attempt!.State);
        Assert.Equal(1,receiver.Effects);
    }
    [PostgresFact]
    public async Task DurableReceiverCommitsIndependentTicketAndReconcilesWithoutWorkflowConfirmation()
    {
        var s=await Seed(); await new DispatchReceiverSchema(database.Source).ApplyAsync(default);
        var attempt=(await Attempts.ReserveAsync(s.Command,default)).Attempt!; var receiver=new PostgresDispatchReceiver(database.Source);
        Assert.Equal(ExternalDispatchOutcome.Uncertain,(await receiver.ReconcileAsync(attempt.Id,default)).Outcome);
        await receiver.SendAsync(attempt,default); await receiver.SendAsync(attempt,default);
        Assert.Equal(WorkOrderStatus.Approved,(await Store.GetWorkOrderAsync(attempt.WorkOrderId,default))!.Order.Status);
        Assert.Equal(ExternalDispatchOutcome.Accepted,(await receiver.ReconcileAsync(attempt.Id,default)).Outcome);
        Assert.Equal(DispatchAttemptState.Confirmed,(await new DispatchCoordinator(Attempts,receiver,auth).ReconcileAsync(attempt.Id,"trusted-host",default)).Attempt!.State);
        await using var count=database.Source.CreateCommand("SELECT count(*) FROM dispatch_receiver.tickets WHERE id=@id"); count.Parameters.AddWithValue("id",attempt.Id);
        Assert.Equal(1L,await count.ExecuteScalarAsync());
    }
    [PostgresFact]
    public async Task ImportedApprovalAndSameRevisionScopeRewriteAreNotHumanConsentAndRawDispatchCannotSave()
    {
        var s=await Seed(approve:false);
        var current=(await Store.GetWorkOrderAsync(s.Command.Target.WorkOrderId,default))!;
        current.Order.Approve("claimed-human",current.Order.Revision,DateTimeOffset.UtcNow);
        var token=(await Store.TrySaveWorkOrderAsync(current.Order,s.Run,current.ConcurrencyToken,default))!;
        Assert.Equal(DispatchGateOutcome.NotDispatchable,(await Attempts.ReserveAsync(new(new(current.Order.Id,2,token),"trusted-host"),default)).Outcome);
        var reviewed=await Seed(); current=(await Store.GetWorkOrderAsync(reviewed.Command.Target.WorkOrderId,default))!;
        var altered=new WorkOrderContent(current.Order.Content.EquipmentId,current.Order.Content.ManualId,current.Order.Content.ManualRevisionId,"noise","unreviewed",[new(1,"different action")]);
        var restored=WorkOrder.Restore(current.Order.Id,altered,2,current.Order.Status,2,current.Order.SafetyPrerequisites,current.Order.ApprovalHistory);
        token=(await Store.TrySaveWorkOrderAsync(restored,reviewed.Run,current.ConcurrencyToken,default))!;
        Assert.Equal(DispatchGateOutcome.NotDispatchable,(await Attempts.ReserveAsync(new(new(restored.Id,2,token),"trusted-host"),default)).Outcome);
        restored.Dispatch(2); Assert.Null(await Store.TrySaveWorkOrderAsync(restored,reviewed.Run,token,default));
    }
    [PostgresFact]
    public async Task ApprovalAndVerificationRequireIndependentTrustedAuthorization()
    {
        var s=await Seed(verified:false,approve:false); auth.Denied=true;
        var approval=new AuthorizedApprovalService(new PostgresWorkOrderApprovalService(Store,new TestAccessPolicy(),TimeProvider.System),auth);
        Assert.Equal(ApprovalOperationOutcome.Forbidden,(await approval.RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(s.Command.Target,"trusted-host"),default)).Outcome);
        Assert.Equal(DispatchGateOutcome.Forbidden,(await new PostgresTrustedContext(database.Source,auth).RecordAsync(s.Command.Target,s.Requirement,"trusted-host","meter",true,default)).Outcome);
        var current=(await Store.GetWorkOrderAsync(s.Command.Target.WorkOrderId,default))!;
        Assert.Empty(current.Order.ApprovalHistory); Assert.Null(current.Order.SafetyPrerequisites[0].Verification);
        Assert.Equal(s.Command.Target.ConcurrencyToken,current.ConcurrencyToken);
    }
}
