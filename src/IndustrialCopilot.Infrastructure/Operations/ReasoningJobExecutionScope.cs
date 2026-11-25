using IndustrialCopilot.Application.Abstractions.Jobs;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class ReasoningJobExecutionScope(Func<string,Guid,bool> authorized,Microsoft.Extensions.Logging.ILogger? log=null) : IReasoningJobExecutionScope
{
    private readonly AsyncLocal<ReasoningJobClaim?> current=new();
    public ReasoningJobClaim? Current=>current.Value;
    public IDisposable Enter(ReasoningJobClaim claim)
    {
        if(!authorized(claim.ActorId,claim.Request.Input.Candidates[0].EquipmentId))
        {
            // Ids only: never the actor secret, bearer token or credentials.
            log?.LogWarning("Reasoning job {JobId} attempt {Attempt} denied: its actor is not authorized for the equipment",claim.JobId,claim.Attempt);
            throw new UnauthorizedAccessException("Job execution is no longer authorized.");
        }
        var previous=current.Value; var correlation=HostAccess.Correlation.Value;
        current.Value=claim; HostAccess.Correlation.Value=claim.Request.CorrelationId;
        return new Exit(()=>{current.Value=previous;HostAccess.Correlation.Value=correlation;});
    }
    private sealed class Exit(Action action) : IDisposable { public void Dispose()=>action(); }
    internal async Task FenceAsync(NpgsqlConnection c,NpgsqlTransaction t,CancellationToken ct)
    {
        if(Current is not {} claim) return;
        // Lock before business rows. Cancellation, claims and all job mutations take this same lock first.
        await using var cmd=OperationalSql.Command(c,t,"SELECT 1 FROM operations.reasoning_jobs WHERE id=@id AND status=2 AND lease_token=@token AND lease_until>clock_timestamp() AND NOT cancellation_requested FOR UPDATE",("id",claim.JobId),("token",claim.LeaseToken));
        if(await cmd.ExecuteScalarAsync(ct) is null) throw new JobLeaseLostException();
    }
}
