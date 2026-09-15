using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using Npgsql;
using NpgsqlTypes;
namespace IndustrialCopilot.Infrastructure.Knowledge;

/// <summary>Per-attempt history avoids last-writer status races. A dedicated session is the liveness marker,
/// not a transaction/lock held across extraction or model calls. Dead sessions read as Interrupted.</summary>
public sealed class PostgresIngestionReports : IIngestionReports, IAsyncDisposable, IDisposable
{
    private readonly NpgsqlDataSource source;
    public PostgresIngestionReports(string connectionString)
    {
        source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, IncludeErrorDetail = false }.ConnectionString);
    }
    public ValueTask DisposeAsync() => source.DisposeAsync();
    public void Dispose() => source.Dispose();
    public async Task<IIngestionAttempt> BeginAsync(DocumentProcessingRequest request, CancellationToken token)
    {
        // Pooling must be disabled: disposal must end the backend, not leave a live pooled liveness marker.
        var connection = source.CreateConnection();
        try
        {
            await connection.OpenAsync(token).ConfigureAwait(false);
            var id = Guid.NewGuid();
            await using var command = new NpgsqlCommand("""
                INSERT INTO knowledge.ingestion_attempts(id,document_id,revision_id,metadata,state,stage,backend_pid,backend_start)
                SELECT @id,@doc,@rev,@metadata,1,1,pid,backend_start FROM pg_stat_activity WHERE pid=pg_backend_pid()
                """, connection);
            command.Parameters.AddWithValue("id", id); command.Parameters.AddWithValue("doc", request.DocumentId);
            command.Parameters.AddWithValue("rev", request.ManualRevisionId);
            command.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, request.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(request.Metadata));
            await command.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            return new Attempt(id, connection);
        }
        catch { await connection.DisposeAsync().ConfigureAwait(false); throw; }
    }
    public async Task<IReadOnlyList<IngestionReport>> ReadAsync(Guid documentId, Guid revisionId, CancellationToken token)
    {
        await using var command = source.CreateCommand("""
            SELECT id,metadata,CASE WHEN state=1 AND NOT EXISTS
                (SELECT 1 FROM pg_stat_activity a WHERE a.pid=i.backend_pid AND a.backend_start=i.backend_start)
                THEN 4 ELSE state END,stage,failure,started_at,updated_at,pages,chunks
            FROM knowledge.ingestion_attempts i WHERE document_id=@doc AND revision_id=@rev ORDER BY started_at DESC,id LIMIT 100
            """);
        command.Parameters.AddWithValue("doc", documentId); command.Parameters.AddWithValue("rev", revisionId);
        var result = new List<IngestionReport>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            result.Add(new(reader.GetGuid(0), documentId, revisionId,
                reader.IsDBNull(1) ? null : JsonSerializer.Deserialize<DocumentMetadata>(reader.GetString(1)),
                (IngestionState)reader.GetInt32(2), (IngestionStage)reader.GetInt32(3), reader.IsDBNull(4) ? null : (IngestionFailure)reader.GetInt32(4),
                reader.GetFieldValue<DateTimeOffset>(5), reader.GetFieldValue<DateTimeOffset>(6),
                reader.IsDBNull(7) ? null : reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetInt32(8)));
        return result.AsReadOnly();
    }
    private sealed class Attempt(Guid id, NpgsqlConnection connection) : IIngestionAttempt
    {
        public Guid Id => id;
        public async Task AdvanceAsync(IngestionStage stage, int? pages, int? chunks, CancellationToken token)
        {
            await using var cmd = new NpgsqlCommand("""
                UPDATE knowledge.ingestion_attempts SET stage=@stage,pages=coalesce(@pages,pages),chunks=coalesce(@chunks,chunks),updated_at=clock_timestamp()
                WHERE id=@id AND state=1 AND stage<=@stage
                """, connection);
            cmd.Parameters.AddWithValue("stage", (int)stage); cmd.Parameters.AddWithValue("id", id);
            cmd.Parameters.AddWithValue("pages", NpgsqlDbType.Integer, (object?)pages ?? DBNull.Value);
            cmd.Parameters.AddWithValue("chunks", NpgsqlDbType.Integer, (object?)chunks ?? DBNull.Value);
            if (await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false) != 1) throw new InvalidOperationException("Ingestion attempt is not active.");
        }
        public async Task FailAsync(IngestionFailure failure, CancellationToken token)
        {
            await using var cmd = new NpgsqlCommand("UPDATE knowledge.ingestion_attempts SET state=3,failure=@failure,updated_at=clock_timestamp() WHERE id=@id AND state=1", connection);
            cmd.Parameters.AddWithValue("failure", (int)failure); cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(token).ConfigureAwait(false);
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
