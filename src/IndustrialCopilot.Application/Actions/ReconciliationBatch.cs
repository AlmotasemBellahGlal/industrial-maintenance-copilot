namespace IndustrialCopilot.Application.Actions;

/// <summary>Claims a bounded set for inspection by advancing durable eligibility, not dispatch authority.
/// A crashed batch becomes discoverable again after the defer period. Reconciliation still owns the attempt lock.</summary>
public interface IReconciliationDiscovery
{
    Task<IReadOnlyList<Guid>> DiscoverAsync(int batchSize,TimeSpan deferFor,CancellationToken cancellationToken);
}
public sealed record ReconciliationBatchResult(int Discovered,int Inspected,int Failed);
public sealed class ReconciliationBatch(IReconciliationDiscovery discovery,DispatchCoordinator coordinator)
{
    public async Task<ReconciliationBatchResult> ExecuteAsync(string actor,int size,int concurrency,TimeSpan deferFor,TimeSpan attemptTimeout,CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        if(size is <1 or >100 || concurrency is <1 or >16 || attemptTimeout<TimeSpan.FromSeconds(1) || attemptTimeout>TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(size));
        var ids=await discovery.DiscoverAsync(size,deferFor,ct); var completed=0; var failed=0;
        await Parallel.ForEachAsync(ids,new ParallelOptions{MaxDegreeOfParallelism=concurrency,CancellationToken=ct},async(id,token)=>
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(attemptTimeout);
            try { await coordinator.ReconcileAsync(id,actor,timeout.Token); Interlocked.Increment(ref completed); }
            catch(OperationCanceledException) when(token.IsCancellationRequested) { throw; }
            catch { Interlocked.Increment(ref failed); }
        });
        return new(ids.Count,completed,failed);
    }
}
