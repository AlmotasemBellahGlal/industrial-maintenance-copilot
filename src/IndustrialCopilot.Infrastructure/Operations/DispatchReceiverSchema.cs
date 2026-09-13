using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Explicit deployment of the demonstration receiver, independently of workflow migration.</summary>
public sealed class DispatchReceiverSchema(NpgsqlDataSource source)
{
    public async Task ApplyAsync(CancellationToken ct)
    {
        await using var command=source.CreateCommand("""
            CREATE SCHEMA IF NOT EXISTS dispatch_receiver;
            CREATE TABLE IF NOT EXISTS dispatch_receiver.tickets(
                id uuid PRIMARY KEY, payload jsonb NOT NULL, accepted_at timestamptz NOT NULL DEFAULT now());
            """);
        await command.ExecuteNonQueryAsync(ct);
    }
}
