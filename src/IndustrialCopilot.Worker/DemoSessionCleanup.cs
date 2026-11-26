using IndustrialCopilot.Infrastructure.Operations;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IndustrialCopilot.Worker;

/// <summary>Removes expired anonymous demo sessions and the data they own. Never touches seeded data; no volume wipe.</summary>
public sealed class DemoSessionCleanup(IDemoSessions sessions,ILogger<DemoSessionCleanup> log,TimeSpan? interval=null) : BackgroundService
{
    private readonly TimeSpan every=interval??TimeSpan.FromSeconds(60);
    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        while(!stopping.IsCancellationRequested)
        {
            try
            {
                int removed;
                do { removed=await sessions.PurgeExpiredAsync(20,stopping); if(removed>0) log.LogInformation("Removed {Count} expired demo sessions.",removed); }
                while(removed==20&&!stopping.IsCancellationRequested);
            }
            catch(OperationCanceledException) when(stopping.IsCancellationRequested) {return;}
            catch(Exception) { log.LogWarning("Demo session cleanup failed; it will retry."); }
            try { await Task.Delay(every,stopping); } catch(OperationCanceledException) {return;}
        }
    }
}
