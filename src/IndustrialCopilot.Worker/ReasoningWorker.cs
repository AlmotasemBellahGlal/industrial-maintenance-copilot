using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Jobs;

namespace IndustrialCopilot.Worker;

public sealed record ReasoningSchedule
{
    public IReadOnlyCollection<Guid> EquipmentIds {get;}
    public int Concurrency {get;}
    public TimeSpan Lease {get;}
    public TimeSpan PollInterval {get;}
    public ReasoningSchedule(IEnumerable<Guid> equipmentIds,int concurrency=2,int leaseSeconds=30,int pollSeconds=1)
    {
        var ids=equipmentIds.Distinct().ToArray();
        if(ids.Length==0 || ids.Contains(Guid.Empty)||concurrency is <1 or >4||leaseSeconds is <4 or >300||pollSeconds is <1 or >60)throw new ArgumentException("Invalid reasoning schedule.");
        EquipmentIds=Array.AsReadOnly(ids);Concurrency=concurrency;Lease=TimeSpan.FromSeconds(leaseSeconds);PollInterval=TimeSpan.FromSeconds(pollSeconds);
    }
}
/// <summary>Separate from dispatch reconciliation. Claims only available execution capacity.</summary>
public sealed class ReasoningWorker(IReasoningJobStore jobs,ReasoningJobProcessor processor,ReasoningSchedule schedule,ILogger<ReasoningWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var owner="reasoning-"+Guid.NewGuid().ToString("N");var active=new List<Task>();
        try
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                active.RemoveAll(t=>t.IsCompleted);
                try
                {
                    while(active.Count<schedule.Concurrency && !stoppingToken.IsCancellationRequested)
                    {
                        var claim=await jobs.ClaimAsync(owner,schedule.EquipmentIds,schedule.Lease,stoppingToken);
                        if(claim is null)break;
                        active.Add(Process(claim));
                    }
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch {logger.LogWarning("Reasoning discovery unavailable; durable jobs remain recoverable");}
                await Task.Delay(schedule.PollInterval,stoppingToken);
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { }
        finally {await Task.WhenAll(active);}
        async Task Process(ReasoningJobClaim claim)
        {
            try {await processor.ExecuteAsync(claim,schedule.Lease,stoppingToken);}
            catch {logger.LogWarning("Reasoning attempt {JobId}/{Attempt} interrupted; durable recovery remains authoritative",claim.JobId,claim.Attempt);}
        }
    }
}
