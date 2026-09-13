using Npgsql;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Sanitized technical storage failure; never contains SQL or stored payloads.</summary>
public sealed class OperationalStoreException() : Exception("Operational storage operation failed.");

public sealed class OperationalSchema(NpgsqlDataSource source)
{
    public async Task ApplyAsync(CancellationToken token)
    {
        await OperationalSql.Run(source, async (connection, transaction) =>
        {
            await OperationalSql.Execute(connection, transaction, "SELECT pg_advisory_xact_lock(1380007986)", token);
            using var resource = typeof(OperationalSchema).Assembly.GetManifestResourceStream("IndustrialCopilot.Infrastructure.Operations.Migrations.001_operations.sql")!;
            using var reader = new StreamReader(resource);
            await OperationalSql.Execute(connection, transaction, await reader.ReadToEndAsync(token), token);
            using var proposalResource = typeof(OperationalSchema).Assembly.GetManifestResourceStream("IndustrialCopilot.Infrastructure.Operations.Migrations.002_proposals.sql")!;
            using var proposalReader = new StreamReader(proposalResource);
            await OperationalSql.Execute(connection, transaction, await proposalReader.ReadToEndAsync(token), token);
            using var dispatchResource = typeof(OperationalSchema).Assembly.GetManifestResourceStream("IndustrialCopilot.Infrastructure.Operations.Migrations.003_dispatch.sql")!;
            using var dispatchReader = new StreamReader(dispatchResource);
            await OperationalSql.Execute(connection, transaction, await dispatchReader.ReadToEndAsync(token), token);
            using var recoveryResource = typeof(OperationalSchema).Assembly.GetManifestResourceStream("IndustrialCopilot.Infrastructure.Operations.Migrations.004_reconciliation.sql")!;
            using var recoveryReader = new StreamReader(recoveryResource);
            await OperationalSql.Execute(connection, transaction, await recoveryReader.ReadToEndAsync(token), token);
            return true;
        }, token);
    }
}

internal static class OperationalSql
{
    internal static async Task<T> Run<T>(NpgsqlDataSource source, Func<NpgsqlConnection,NpgsqlTransaction,Task<T>> operation, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();
            await using var connection = await source.OpenConnectionAsync(token);
            await using var transaction = await connection.BeginTransactionAsync(token);
            var result = await operation(connection, transaction);
            await transaction.CommitAsync(token);
            return result;
        }
        catch (Exception e) when (e is NpgsqlException or IOException or TimeoutException)
        { token.ThrowIfCancellationRequested(); throw new OperationalStoreException(); }
    }

    internal static NpgsqlCommand Command(NpgsqlConnection c, NpgsqlTransaction t, string sql, params (string,object?)[] args)
    {
        var command = new NpgsqlCommand(sql,c,t);
        foreach (var (name,value) in args) command.Parameters.AddWithValue(name,value ?? DBNull.Value);
        return command;
    }
    internal static async Task<int> Execute(NpgsqlConnection c, NpgsqlTransaction t, string sql, CancellationToken ct, params (string,object?)[] args)
    {
        await using var cmd = Command(c,t,sql,args);
        return await cmd.ExecuteNonQueryAsync(ct);
    }
}
