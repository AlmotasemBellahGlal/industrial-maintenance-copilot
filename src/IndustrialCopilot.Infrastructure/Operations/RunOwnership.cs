using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public interface IRunOwnership
{
    Task RecordAsync(Guid runId,string actor,CancellationToken ct);
    Task<bool> IsOwnerAsync(Guid runId,string actor,CancellationToken ct);
}

/// <summary>Owner-scoping for reads. The owner is the authenticated actor that started the run; it is never client supplied.</summary>
public sealed class RunOwnership(NpgsqlDataSource source) : IRunOwnership
{
    public Task RecordAsync(Guid runId,string actor,CancellationToken ct)
    {
        if(runId==Guid.Empty) throw new ArgumentException("Run identity required.");
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        return Run(source,async(c,t)=>{await Execute(c,t,"INSERT INTO operations.run_owners(run_id,actor) VALUES(@run,@actor) ON CONFLICT (run_id) DO NOTHING",ct,("run",runId),("actor",actor));return true;},ct);
    }
    public Task<bool> IsOwnerAsync(Guid runId,string actor,CancellationToken ct)=>
        Run(source,async(c,t)=>
        {
            await using var cmd=Command(c,t,"SELECT 1 FROM operations.run_owners WHERE run_id=@run AND actor=@actor",("run",runId),("actor",actor));
            return await cmd.ExecuteScalarAsync(ct) is not null;
        },ct);
}
