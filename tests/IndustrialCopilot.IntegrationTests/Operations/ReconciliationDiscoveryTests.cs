using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Application.Actions;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
namespace IndustrialCopilot.IntegrationTests.Operations;

public class ReconciliationDiscoveryTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private sealed class Auth : IActionAuthorization {public Task<bool> AuthorizeAsync(string a,TrustedAction action,Guid id,CancellationToken ct)=>Task.FromResult(true);}
    private readonly Auth auth=new();
    private async Task<DispatchAttempt[]> Seed(int count)
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        await using(var postpone=database.Source.CreateCommand("UPDATE operations.dispatch_attempts SET next_reconciliation_at=now()+interval '1 hour'"))await postpone.ExecuteNonQueryAsync();
        var store=new PostgresWorkflowStore(database.Source);var attempts=new PostgresDispatchAttemptStore(database.Source,auth);var result=new List<DispatchAttempt>();
        for(var index=0;index<count;index++)
        {
            var order=new WorkOrder(Guid.NewGuid(),new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),"noise","inspect",[new(1,"inspect")]));order.AssessSafety(1,[]);order.SubmitForApproval(2);
            var run=new MaintenanceRun(Guid.NewGuid(),order.Content.EquipmentId,"noise");run.Start();run.WaitForApproval();await store.TrySaveRunAsync(run,null,default);
            var token=(await store.TrySaveWorkOrderAsync(order,run.Id,null,default))!;
            var approved=await new PostgresWorkOrderApprovalService(store,new TestAccessPolicy(),TimeProvider.System).RecordDecisionAsync(RecordWorkOrderDecisionRequest.Approve(new(order.Id,2,token),"supervisor"),default);
            result.Add((await attempts.ReserveAsync(new(approved.Snapshot!.Target,"worker"),default)).Attempt!);
        }
        return result.ToArray();
    }
    [PostgresFact]
    public async Task DiscoveryIsBoundedDeterministicDeferredAndConcurrentClaimsDoNotHotLoop()
    {
        var attempts=await Seed(5);var ids=attempts.Select(a=>a.Id).Order().ToArray();
        await using(var reset=database.Source.CreateCommand("UPDATE operations.dispatch_attempts SET next_reconciliation_at='2000-01-01' WHERE attempt_id=ANY(@ids)")){reset.Parameters.AddWithValue("ids",ids);await reset.ExecuteNonQueryAsync();}
        var discovery=new PostgresReconciliationDiscovery(database.Source);
        Assert.Equal(ids.Take(2),await discovery.DiscoverAsync(2,TimeSpan.FromMinutes(1),default));
        var batches=await Task.WhenAll(discovery.DiscoverAsync(2,TimeSpan.FromMinutes(1),default),new PostgresReconciliationDiscovery(database.Source).DiscoverAsync(2,TimeSpan.FromMinutes(1),default));
        Assert.Equal(3,batches.Sum(b=>b.Count));Assert.Equal(3,batches.SelectMany(b=>b).Distinct().Count());
        Assert.Empty(await discovery.DiscoverAsync(100,TimeSpan.FromMinutes(1),default));
        using var c=new CancellationTokenSource();c.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>discovery.DiscoverAsync(2,TimeSpan.FromMinutes(1),c.Token));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(()=>discovery.DiscoverAsync(101,TimeSpan.FromMinutes(1),default));
    }
    [PostgresFact]
    public async Task RestartFindsAcceptedButUnconfirmedAttemptAndConcurrentWorkersConfirmOnce()
    {
        var attempt=Assert.Single(await Seed(1));await new DispatchReceiverSchema(database.Source).ApplyAsync(default);
        var store=new PostgresDispatchAttemptStore(database.Source,auth);
        await using(var session=(await store.OpenAsync(attempt.Id,default))!)await session.MarkInvocationStartedAsync(default);
        await new PostgresDispatchReceiver(database.Source).SendAsync(attempt,default); // Initiating process dies before internal confirmation.
        var discovered=Assert.Single(await new PostgresReconciliationDiscovery(database.Source).DiscoverAsync(10,TimeSpan.FromSeconds(30),default));Assert.Equal(attempt.Id,discovered);
        var first=new DispatchCoordinator(new PostgresDispatchAttemptStore(database.Source,auth),new PostgresDispatchReceiver(database.Source),auth);
        var second=new DispatchCoordinator(new PostgresDispatchAttemptStore(database.Source,auth),new PostgresDispatchReceiver(database.Source),auth);
        var results=await Task.WhenAll(first.ReconcileAsync(discovered,"worker",default),second.ReconcileAsync(discovered,"worker",default));
        Assert.All(results,r=>Assert.Equal(DispatchAttemptState.Confirmed,r.Attempt!.State));
        Assert.Equal(WorkOrderStatus.Dispatched,(await new PostgresWorkflowStore(database.Source).GetWorkOrderAsync(attempt.WorkOrderId,default))!.Order.Status);
        Assert.Equal(MaintenanceRunStatus.Completed,(await new PostgresWorkflowStore(database.Source).GetRunAsync(attempt.RunId!.Value,default))!.Run.Status);
        await using var audit=database.Source.CreateCommand("SELECT count(*) FROM operations.dispatch_events WHERE attempt_id=@id AND category='Accepted'");audit.Parameters.AddWithValue("id",attempt.Id);Assert.Equal(1L,await audit.ExecuteScalarAsync());
        Assert.Empty(await new PostgresReconciliationDiscovery(database.Source).DiscoverAsync(100,TimeSpan.FromSeconds(30),default));
    }
}
