using IndustrialCopilot.Application.Abstractions.Actions;

namespace IndustrialCopilot.Application.Actions;

public sealed class DispatchCoordinator
{
    private readonly IDispatchAttemptStore store;
    private readonly IExternalDispatch adapter;
    private readonly IActionAuthorization authorization;
    public DispatchCoordinator(IDispatchAttemptStore store,IExternalDispatch adapter,IActionAuthorization authorization)
    {
        ArgumentNullException.ThrowIfNull(store); ArgumentNullException.ThrowIfNull(adapter); ArgumentNullException.ThrowIfNull(authorization);
        this.store=store; this.adapter=adapter; this.authorization=authorization;
    }
    public async Task<DispatchReservation> DispatchAsync(DispatchCommand command,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command); ct.ThrowIfCancellationRequested();
        if(!await authorization.AuthorizeAsync(command.ActorId,TrustedAction.Dispatch,command.Target.WorkOrderId,ct))
            return new(DispatchGateOutcome.Forbidden,null);
        var reserved=await store.ReserveAsync(command,ct);
        if(reserved.Attempt is null || reserved.Outcome!=DispatchGateOutcome.Ready) return reserved;
        return await Execute(reserved.Attempt.Id,command.ActorId,false,false,ct);
    }
    public Task<DispatchReservation> ReconcileAsync(Guid attemptId,string actorId,CancellationToken ct) => Execute(attemptId,actorId,true,false,ct);
    public Task<DispatchReservation> RetryAsync(Guid attemptId,string actorId,CancellationToken ct) => Execute(attemptId,actorId,false,true,ct);

    private async Task<DispatchReservation> Execute(Guid id,string actor,bool reconcile,bool retry,CancellationToken ct)
    {
        if(id==Guid.Empty) throw new ArgumentException("Attempt identity required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(actor); ct.ThrowIfCancellationRequested();
        var known=await store.GetAsync(id,ct);
        if(known is null) return new(DispatchGateOutcome.NotFound,null);
        if(!await authorization.AuthorizeAsync(actor,TrustedAction.Dispatch,known.WorkOrderId,ct)) return new(DispatchGateOutcome.Forbidden,null);
        await using var session=await store.OpenAsync(id,ct);
        if(session is null) return new(DispatchGateOutcome.NotFound,null);
        if(!await authorization.AuthorizeAsync(actor,TrustedAction.Dispatch,session.Attempt.WorkOrderId,ct)) return new(DispatchGateOutcome.Forbidden,null);
        if(session.Attempt.State==DispatchAttemptState.Confirmed) return new(DispatchGateOutcome.Ready,session.Attempt);
        if(retry && !await session.RestartAsync(actor,ct)) return new(DispatchGateOutcome.Conflict,session.Attempt);
        if(!reconcile && (session.Attempt.State!=DispatchAttemptState.Pending || session.Attempt.InvocationStarted))
            return new(DispatchGateOutcome.Ready,session.Attempt);
        if(reconcile && session.Attempt.State==DispatchAttemptState.DefinitivelyFailed) return new(DispatchGateOutcome.Ready,session.Attempt);
        ct.ThrowIfCancellationRequested();
        if(!reconcile) await session.MarkInvocationStartedAsync(ct); // Durable BEFORE transmission.
        ExternalDispatchResult result;
        try
        {
            ct.ThrowIfCancellationRequested();
            result=reconcile ? await adapter.ReconcileAsync(id,ct) : await adapter.SendAsync(session.Attempt,ct);
            ct.ThrowIfCancellationRequested();
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested)
        {
            try
            {
                using var cleanup=new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await session.RecordAsync(ExternalDispatchResult.Uncertain(),actor,cleanup.Token);
            }
            finally { ct.ThrowIfCancellationRequested(); }
            throw;
        }
        catch { result=ExternalDispatchResult.Uncertain(); }
        // On persistence failure the attempt stays Pending/Uncertain with InvocationStarted=true.
        // Reconciliation, never a new send/key, recovers external acceptance.
        await session.RecordAsync(result,actor,ct);
        return new(DispatchGateOutcome.Ready,session.Attempt);
    }
}
