using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Reasoning;

namespace IndustrialCopilot.Application.Jobs;

/// <summary>One leased attempt of the existing pipeline. Shutdown/lost ownership is recovery, not user cancellation.</summary>
public sealed class ReasoningJobProcessor(IReasoningJobStore jobs,MaintenanceOrchestrator orchestrator,IReasoningJobExecutionScope scope)
{
    public async Task ExecuteAsync(ReasoningJobClaim claim,TimeSpan lease,CancellationToken shutdown)
    {
        using var work=CancellationTokenSource.CreateLinkedTokenSource(shutdown);
        using var monitoring=new CancellationTokenSource();
        var monitor=Monitor();
        try
        {
            using var bound=scope.Enter(claim);
            var result=await orchestrator.ExecuteAsync(claim.Request,work.Token,durableAttempt:true,
                durableProgress:(p,ct)=>jobs.ReportAsync(claim,p,ct));
            using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await jobs.CompleteAsync(claim,result,finish.Token);
        }
        catch(JobLeaseLostException)
        {
            using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await jobs.ReleaseAsync(claim,finish.Token);
        }
        catch(OperationCanceledException) when(work.IsCancellationRequested)
        {
            using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Store acknowledges persisted user cancellation; otherwise returns work to a bounded recovery schedule.
            await jobs.ReleaseAsync(claim,finish.Token);
        }
        catch(UnauthorizedAccessException)
        {
            using var finish=new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await jobs.CompleteAsync(claim,new(MaintenanceReasoningOutcome.Failed,claim.Request.RunId,null,TraceComplete:false),finish.Token);
        }
        finally
        {
            monitoring.Cancel();
            await monitor;
        }
        async Task Monitor()
        {
            try
            {
                using var timer=new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100,lease.TotalMilliseconds/4)));
                while(await timer.WaitForNextTickAsync(monitoring.Token))
                {
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(monitoring.Token);
                    timeout.CancelAfter(TimeSpan.FromMilliseconds(lease.TotalMilliseconds/3));
                    if(await jobs.RenewAsync(claim,lease,timeout.Token)!=JobLeaseState.Owned) { work.Cancel(); return; }
                }
            }
            catch(OperationCanceledException) when(monitoring.IsCancellationRequested) { }
            catch { work.Cancel(); }
        }
    }
}
