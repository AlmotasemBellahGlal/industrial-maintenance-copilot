using IndustrialCopilot.Application.Actions;

namespace IndustrialCopilot.Worker;

public sealed record ReconciliationSchedule
{
    public TimeSpan Interval { get; }
    public int BatchSize { get; }
    public int Concurrency { get; }
    public TimeSpan AttemptTimeout { get; }
    public ReconciliationSchedule(int intervalSeconds=30,int batchSize=20,int concurrency=4,int attemptTimeoutSeconds=30)
    {
        if(intervalSeconds is <5 or >3600 || batchSize is <1 or >100 || concurrency is <1 or >16 || attemptTimeoutSeconds is <1 or >300) throw new ArgumentException("Invalid reconciliation schedule.");
        Interval=TimeSpan.FromSeconds(intervalSeconds); BatchSize=batchSize; Concurrency=concurrency; AttemptTimeout=TimeSpan.FromSeconds(attemptTimeoutSeconds);
    }
}
public sealed class Worker(ReconciliationBatch batch,ReconciliationSchedule schedule,ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer=new PeriodicTimer(schedule.Interval);
        while(await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var result=await batch.ExecuteAsync("reconciliation-worker",schedule.BatchSize,schedule.Concurrency,schedule.Interval,schedule.AttemptTimeout,stoppingToken);
                logger.LogInformation("Reconciliation inspected {Inspected}, failed {Failed} of {Discovered} attempts",result.Inspected,result.Failed,result.Discovered);
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch { logger.LogWarning("Reconciliation batch unavailable; durable attempts remain recoverable"); }
        }
    }
}
