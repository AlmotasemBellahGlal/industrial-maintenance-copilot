using System.Data;
using System.Text.Json;
using System.Globalization;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using Npgsql;
using NpgsqlTypes;

namespace IndustrialCopilot.Infrastructure.Knowledge;

public sealed class PostgresKnowledgeStore(NpgsqlDataSource dataSource, KnowledgeStoreOptions options, ILlmProvider embeddings)
    : IKnowledgeIndex, IRetrievalService
{
    public async Task ReplaceRevisionAsync(RevisionIndexRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        if (request.EmbeddingProfile != options.Space.Profile) throw Incompatible();
        foreach (var item in request.Chunks) options.Space.ValidateVector(item.Vector);
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (var register = new NpgsqlCommand("""
                INSERT INTO knowledge.profiles(profile,binding,dimensions) VALUES (@profile,@binding,@dimensions)
                ON CONFLICT DO NOTHING
                """, connection, transaction))
            {
                AddSpace(register);
                register.Parameters.AddWithValue("binding", options.Binding);
                await register.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            await CheckSpaceAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            await using (var revision = new NpgsqlCommand("""
                INSERT INTO knowledge.revisions(document_id,revision_id,profile,dimensions)
                VALUES (@document,@revision,@profile,@dimensions) ON CONFLICT DO NOTHING;
                SELECT 1 FROM knowledge.revisions WHERE document_id=@document AND revision_id=@revision FOR UPDATE;
                """, connection, transaction))
            {
                AddRevision(revision, request); AddSpace(revision);
                await revision.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            // Both tables have secondary unique constraints used by foreign keys. During concurrent
            // first inserts, arbitrate all unique conflicts before taking the stable revision lock.
            // Profile compatibility is still checked independently above.
            await using (var replace = new NpgsqlCommand("""
                DELETE FROM knowledge.chunks WHERE document_id=@document AND revision_id=@revision;
                UPDATE knowledge.revisions SET profile=@profile,dimensions=@dimensions WHERE document_id=@document AND revision_id=@revision;
                """, connection, transaction))
            {
                AddRevision(replace, request); AddSpace(replace);
                await replace.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            foreach (var item in request.Chunks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var insert = new NpgsqlCommand("""
                    INSERT INTO knowledge.chunks(document_id,revision_id,chunk_id,profile,dimensions,locator,content,embedding,metadata,page,section)
                    VALUES (@document,@revision,@chunk,@profile,@dimensions,@locator,@content,CAST(@vector AS vector),@metadata,@page,@section)
                    """, connection, transaction);
                AddRevision(insert, request); AddSpace(insert);
                insert.Parameters.AddWithValue("chunk", item.Chunk.ChunkId);
                insert.Parameters.AddWithValue("locator", item.Chunk.Locator);
                insert.Parameters.AddWithValue("content", item.Chunk.Content);
                insert.Parameters.AddWithValue("vector", VectorText(item.Vector));
                insert.Parameters.AddWithValue("metadata", NpgsqlDbType.Jsonb, item.Chunk.Metadata is null ? DBNull.Value : JsonSerializer.Serialize(item.Chunk.Metadata));
                insert.Parameters.AddWithValue("page", NpgsqlDbType.Integer, (object?)item.Chunk.Page ?? DBNull.Value);
                insert.Parameters.AddWithValue("section", NpgsqlDbType.Text, (object?)item.Chunk.Section ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            if (request.IngestionAttemptId is {} attemptId)
            {
                await using var complete = new NpgsqlCommand("""
                    UPDATE knowledge.ingestion_attempts i SET state=2,updated_at=clock_timestamp()
                    WHERE id=@attempt AND document_id=@document AND revision_id=@revision AND state=1 AND stage=5
                    AND EXISTS(SELECT 1 FROM pg_stat_activity a WHERE a.pid=i.backend_pid AND a.backend_start=i.backend_start)
                    """, connection, transaction);
                complete.Parameters.AddWithValue("attempt", attemptId); AddRevision(complete, request);
                if (await complete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false) != 1)
                    throw new InvalidOperationException("Ingestion attempt is no longer active.");
            }
            // Cancellation/any failure before commit rolls the whole revision back on disposal.
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or IOException)
        { cancellationToken.ThrowIfCancellationRequested(); throw new KnowledgeStoreException(KnowledgeFailure.StorageUnavailable); }
    }

    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery query, RetrievalMode mode, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        cancellationToken.ThrowIfCancellationRequested();
        IReadOnlyList<float>? vector = null;
        if (mode != RetrievalMode.Keyword)
        {
            var result = await embeddings.GenerateEmbeddingsAsync(new EmbeddingRequest([query.QueryText]), cancellationToken).ConfigureAwait(false);
            options.Space.Validate(result, 1);
            vector = result.Vectors[0];
        }
        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            // Profile checks and both rankings see the same committed revision snapshot.
            await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken).ConfigureAwait(false);
            var exists = await CheckSpaceAsync(connection, transaction, cancellationToken).ConfigureAwait(false);
            if (query.DocumentId is not null || query.ManualRevisionId is not null)
            {
                await using var mismatch = new NpgsqlCommand("""
                    SELECT EXISTS(SELECT 1 FROM knowledge.revisions WHERE profile<>@profile
                    AND (@document IS NULL OR document_id=@document) AND (@revision IS NULL OR revision_id=@revision))
                    """, connection, transaction);
                mismatch.Parameters.AddWithValue("profile", options.Space.Profile); AddFilters(mismatch, query);
                if ((bool)(await mismatch.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))!) throw Incompatible();
            }
            if (!exists) return Array.Empty<RetrievalResult>();
            var limit = mode == RetrievalMode.Hybrid ? Math.Max(query.TopK, options.HybridCandidates) : query.TopK;
            IReadOnlyList<RetrievalResult> keyword = Array.Empty<RetrievalResult>(), dense = Array.Empty<RetrievalResult>();
            if (mode != RetrievalMode.Dense) keyword = await RankAsync(connection, transaction, query, null, limit, cancellationToken).ConfigureAwait(false);
            if (mode != RetrievalMode.Keyword) dense = await RankAsync(connection, transaction, query, vector, limit, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return mode switch { RetrievalMode.Keyword => keyword, RetrievalMode.Dense => dense, _ => ReciprocalRankFusion.Combine(keyword, dense, query.TopK) };
        }
        catch (Exception error) when (error is NpgsqlException or TimeoutException or IOException)
        { cancellationToken.ThrowIfCancellationRequested(); throw new KnowledgeStoreException(KnowledgeFailure.StorageUnavailable,error is NpgsqlException {IsTransient:true} ? IndustrialCopilot.Application.Abstractions.AI.DependencyFailureKind.Transient : error is TimeoutException ? IndustrialCopilot.Application.Abstractions.AI.DependencyFailureKind.Timeout : IndustrialCopilot.Application.Abstractions.AI.DependencyFailureKind.Terminal); }
    }

    private async Task<bool> CheckSpaceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = new NpgsqlCommand("SELECT binding,dimensions FROM knowledge.profiles WHERE profile=@profile", connection, transaction);
        command.Parameters.AddWithValue("profile", options.Space.Profile);
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        if (!await reader.ReadAsync(token).ConfigureAwait(false)) return false;
        if (reader.GetString(0) != options.Binding || reader.GetInt32(1) != options.Space.Dimensions) throw Incompatible();
        return true;
    }

    private async Task<IReadOnlyList<RetrievalResult>> RankAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        RetrievalQuery query, IReadOnlyList<float>? vector, int limit, CancellationToken token)
    {
        // SQL alternatives are constants, never supplied by the caller. MATERIALIZED ensures
        // vectors from other profiles/dimensions cannot reach the distance operator.
        // websearch_to_tsquery treats un-quoted terms as OR (tsquery with |), which matches natural-language
        // maintenance questions without requiring every question word to appear in the chunk.
        // plainto_tsquery uses AND and fails entirely when any query word (e.g. "what", "shows") is absent.
        var ranking = vector is null
            ? "SELECT *, ts_rank_cd(search_text,websearch_to_tsquery('simple',@query))::double precision AS score FROM filtered WHERE search_text @@ websearch_to_tsquery('simple',@query)"
            : "SELECT *, 1-(embedding <=> CAST(@vector AS vector)) AS score FROM filtered";
        var threshold = vector is null ? "" : "WHERE score >= @minimum";
        var sql = """
            WITH filtered AS MATERIALIZED (
                SELECT * FROM knowledge.chunks WHERE profile=@profile
                AND (@document IS NULL OR document_id=@document) AND (@revision IS NULL OR revision_id=@revision)
            ), ranked AS (
            """ + ranking + ") SELECT document_id,revision_id,chunk_id,locator,content,score FROM ranked " + threshold +
            " ORDER BY score DESC,document_id,revision_id,chunk_id LIMIT @limit";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("profile", options.Space.Profile); AddFilters(command, query);
        command.Parameters.AddWithValue("limit", limit);
        if (vector is null) command.Parameters.AddWithValue("query", query.QueryText);
        else
        {
            command.Parameters.AddWithValue("vector", VectorText(vector));
            command.Parameters.AddWithValue("minimum", options.MinimumCosineSimilarity);
        }
        var results = new List<RetrievalResult>();
        await using var reader = await command.ExecuteReaderAsync(token).ConfigureAwait(false);
        while (await reader.ReadAsync(token).ConfigureAwait(false))
            results.Add(new(reader.GetGuid(0), reader.GetGuid(1), reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetDouble(5)));
        return results.AsReadOnly();
    }

    private void AddSpace(NpgsqlCommand command)
    {
        command.Parameters.AddWithValue("profile", options.Space.Profile);
        command.Parameters.AddWithValue("dimensions", options.Space.Dimensions);
    }
    private static void AddRevision(NpgsqlCommand command, RevisionIndexRequest request)
    {
        command.Parameters.AddWithValue("document", request.DocumentId);
        command.Parameters.AddWithValue("revision", request.ManualRevisionId);
    }
    private static void AddFilters(NpgsqlCommand command, RetrievalQuery query)
    {
        command.Parameters.AddWithValue("document", NpgsqlDbType.Uuid, (object?)query.DocumentId ?? DBNull.Value);
        command.Parameters.AddWithValue("revision", NpgsqlDbType.Uuid, (object?)query.ManualRevisionId ?? DBNull.Value);
    }
    private static string VectorText(IReadOnlyList<float> vector) => "[" + string.Join(",", vector.Select(v => v.ToString("R", CultureInfo.InvariantCulture))) + "]";
    private static KnowledgeStoreException Incompatible() => new(KnowledgeFailure.IncompatibleEmbeddingSpace);
}
