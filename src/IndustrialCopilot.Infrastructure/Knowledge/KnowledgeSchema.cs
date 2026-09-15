using Npgsql;

namespace IndustrialCopilot.Infrastructure.Knowledge;

/// <summary>Explicit deployment migration, not DDL on the retrieval request path.</summary>
public sealed class KnowledgeSchema(NpgsqlDataSource dataSource)
{
    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(1380007985)", connection, transaction);
            await lockCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            foreach (var migration in new[] { "001_knowledge.sql", "002_ingestion.sql" })
            {
                using var resource = typeof(KnowledgeSchema).Assembly.GetManifestResourceStream("IndustrialCopilot.Infrastructure.Knowledge.Migrations." + migration)!;
                using var reader = new StreamReader(resource);
                var sql = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or IOException)
        { cancellationToken.ThrowIfCancellationRequested(); throw new KnowledgeStoreException(KnowledgeFailure.StorageUnavailable); }
    }
}
