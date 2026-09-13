using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Actions;
using Npgsql;
using static IndustrialCopilot.Infrastructure.Operations.OperationalSql;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Durable demonstration dispatch inbox, independently committed from workflow confirmation.
/// Acceptance means a dispatch ticket was queued here, not that a real ERP executed it.</summary>
public sealed class PostgresDispatchReceiver(NpgsqlDataSource source) : IExternalDispatch
{
    public Task<ExternalDispatchResult> SendAsync(DispatchAttempt attempt,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        var payload=JsonSerializer.Serialize(new { attempt.WorkOrderId,attempt.Revision,attempt.Content });
        return Run(source,async(c,t)=>
        {
            await Execute(c,t,"INSERT INTO dispatch_receiver.tickets(id,payload) VALUES(@id,CAST(@payload AS jsonb)) ON CONFLICT(id) DO NOTHING",ct,("id",attempt.Id),("payload",payload));
            await using var command=Command(c,t,"SELECT payload=CAST(@payload AS jsonb) FROM dispatch_receiver.tickets WHERE id=@id",("id",attempt.Id),("payload",payload));
            if(await command.ExecuteScalarAsync(ct) is not true) throw new InvalidOperationException("Idempotency key scope mismatch.");
            return ExternalDispatchResult.Accepted(attempt.Id.ToString("D"));
        },ct);
    }
    public Task<ExternalDispatchResult> ReconcileAsync(Guid key,CancellationToken ct) => Run(source,async(c,t)=>
    {
        await using var command=Command(c,t,"SELECT id FROM dispatch_receiver.tickets WHERE id=@id",("id",key));
        return await command.ExecuteScalarAsync(ct) is Guid ? ExternalDispatchResult.Accepted(key.ToString("D")):ExternalDispatchResult.Uncertain();
    },ct);
}
