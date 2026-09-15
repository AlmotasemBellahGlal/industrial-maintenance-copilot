using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresRunTraceStore(NpgsqlDataSource source,OperationalAccessPolicy policy,ReasoningJobExecutionScope? jobScope=null) : IRunTraceStore
{
    public async Task<RunTraceSnapshot?> GetAsync(Guid executionId,CancellationToken ct)
    {
        if(executionId==Guid.Empty) throw new ArgumentException("Execution identity required.");
        ct.ThrowIfCancellationRequested();
        if(!await policy.CanAccessTraceAsync(executionId,false,ct)) return null;
        return await Run(source,(c,t)=>Load(c,t,executionId,false,ct),ct);
    }

    public async Task<bool> TrySaveAsync(RunTraceSnapshot snapshot,long expectedVersion,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if(expectedVersion<0 || snapshot.Version!=checked(expectedVersion+1)) throw new ArgumentException("Invalid expected version.");
        ct.ThrowIfCancellationRequested();
        if(!await policy.CanAccessTraceAsync(snapshot.ExecutionId,true,ct)) throw new UnauthorizedAccessException("Trace access denied.");
        var json=TraceSnapshots.Serialize(snapshot);
        return await Run(source,async(c,t)=>
        {
            if(jobScope is not null) await jobScope.FenceAsync(c,t,ct);
            // INSERT serializes concurrent first writers; the primary key is the arbitration point.
            var inserted=await Execute(c,t,"""
                INSERT INTO operations.traces(execution_id,correlation_id,run_id,version,payload)
                SELECT @id,@correlation,@run,@version,CAST(@payload AS jsonb) WHERE @expected=0
                ON CONFLICT(execution_id) DO NOTHING
                """,ct,("id",snapshot.ExecutionId),("correlation",snapshot.CorrelationId),("run",snapshot.MaintenanceRunId),
                ("version",snapshot.Version),("payload",json),("expected",expectedVersion));
            if(inserted==1) return true;
            var current=await Load(c,t,snapshot.ExecutionId,true,ct);
            if(current is null) return false;
            if(current.Version==snapshot.Version) return TraceSnapshots.Equivalent(current,snapshot);
            if(current.Version!=expectedVersion) return false;
            TraceSnapshots.ValidateUpdate(current,snapshot);
            return await Execute(c,t,"UPDATE operations.traces SET version=@version,run_id=@run,payload=CAST(@payload AS jsonb) WHERE execution_id=@id AND version=@expected",
                ct,("version",snapshot.Version),("run",snapshot.MaintenanceRunId),("payload",json),("id",snapshot.ExecutionId),("expected",expectedVersion))==1;
        },ct);
    }
    private static async Task<RunTraceSnapshot?> Load(NpgsqlConnection c,NpgsqlTransaction t,Guid id,bool locked,CancellationToken ct)
    {
        await using var cmd=Command(c,t,"SELECT payload::text FROM operations.traces WHERE execution_id=@id"+(locked?" FOR UPDATE":""),("id",id));
        var value=await cmd.ExecuteScalarAsync(ct);
        return value is string json?TraceSnapshots.Deserialize(json):null;
    }
}
