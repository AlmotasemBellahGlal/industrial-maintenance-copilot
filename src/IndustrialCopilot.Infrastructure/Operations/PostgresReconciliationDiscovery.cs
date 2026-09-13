using IndustrialCopilot.Application.Actions;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

public sealed class PostgresReconciliationDiscovery(NpgsqlDataSource source) : IReconciliationDiscovery
{
    public Task<IReadOnlyList<Guid>> DiscoverAsync(int batchSize,TimeSpan deferFor,CancellationToken ct)
    {
        if(batchSize is <1 or >100) throw new ArgumentOutOfRangeException(nameof(batchSize));
        if(deferFor<TimeSpan.FromSeconds(5) || deferFor>TimeSpan.FromHours(1)) throw new ArgumentOutOfRangeException(nameof(deferFor));
        return Run<IReadOnlyList<Guid>>(source,async(c,t)=>
        {
            await using var command=Command(c,t,"""
                WITH due AS (
                    SELECT attempt_id,next_reconciliation_at FROM operations.dispatch_attempts
                    WHERE state IN (1,4) AND next_reconciliation_at<=now()
                    ORDER BY next_reconciliation_at,attempt_id LIMIT @size FOR UPDATE SKIP LOCKED
                ), claimed AS (
                    UPDATE operations.dispatch_attempts a SET next_reconciliation_at=now()+@defer
                    FROM due WHERE a.attempt_id=due.attempt_id RETURNING a.attempt_id
                ) SELECT due.attempt_id FROM due JOIN claimed USING(attempt_id) ORDER BY due.next_reconciliation_at,due.attempt_id
                """,("size",batchSize),("defer",deferFor));
            var ids=new List<Guid>(); await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0));
            return ids.AsReadOnly();
        },ct);
    }
}
