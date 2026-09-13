using IndustrialCopilot.Application.Abstractions.Workflow;
using Npgsql;
namespace IndustrialCopilot.Infrastructure.Operations;
public sealed class PostgresWorkflowInspection(NpgsqlDataSource source) : IWorkflowInspection
{
    public async Task<RunLinks> GetLinksAsync(Guid runId,CancellationToken ct)
    {
        if(runId==Guid.Empty) throw new ArgumentException("Run identity required.");
        async Task<IReadOnlyList<Guid>> Read(string sql)
        {
            await using var command=source.CreateCommand(sql); command.Parameters.AddWithValue("id",runId);
            var ids=new List<Guid>(); await using var reader=await command.ExecuteReaderAsync(ct);
            while(await reader.ReadAsync(ct)) ids.Add(reader.GetGuid(0)); return ids.AsReadOnly();
        }
        return new(await Read("SELECT id FROM operations.work_orders WHERE run_id=@id ORDER BY id LIMIT 100"),await Read("SELECT execution_id FROM operations.traces WHERE run_id=@id ORDER BY execution_id LIMIT 100"));
    }
}
