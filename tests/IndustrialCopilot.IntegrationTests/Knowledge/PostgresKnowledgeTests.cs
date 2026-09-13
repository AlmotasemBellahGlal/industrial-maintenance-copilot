using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Infrastructure.Knowledge;
using Npgsql;

namespace IndustrialCopilot.IntegrationTests.Knowledge;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RAG_TEST_POSTGRES")))
            Skip = "Set RAG_TEST_POSTGRES to an isolated pgvector test server; CI provisions one.";
    }
}

public sealed class KnowledgeDatabase : IAsyncLifetime
{
    private readonly string database = "rag_test_" + Guid.NewGuid().ToString("N");
    private NpgsqlDataSource? admin;
    public NpgsqlDataSource Source { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        var connection = Environment.GetEnvironmentVariable("RAG_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connection)) return;
        admin = NpgsqlDataSource.Create(connection);
        // The identifier is generated here, never supplied by configuration/test input.
        await using var create = admin.CreateCommand($"CREATE DATABASE {database}");
        await create.ExecuteNonQueryAsync();
        Source = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connection) { Database = database, IncludeErrorDetail = false }.ConnectionString);
        await new KnowledgeSchema(Source).ApplyAsync(default);
        await new KnowledgeSchema(Source).ApplyAsync(default); // migration is idempotent
    }
    public async Task DisposeAsync()
    {
        if (admin is null) return;
        if (Source is not null) await Source.DisposeAsync();
        await using var drop = admin.CreateCommand($"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        await drop.ExecuteNonQueryAsync();
        await admin.DisposeAsync();
    }
}

public class PostgresKnowledgeTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private sealed class FakeEmbeddings : ILlmProvider
    {
        internal int Calls;
        internal string Model = "test-model";
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Calls++;
            return Task.FromResult(new EmbeddingResult(request.Inputs.Select(_ => (IReadOnlyList<float>)new float[] { 1, 0 }).ToArray(), Model));
        }
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
    }
    private PostgresKnowledgeStore Store(string profile = "test-v1", string binding = "provider-weights-v1", FakeEmbeddings? embeddings = null) =>
        new(database.Source, new KnowledgeStoreOptions(new EmbeddingSpace(profile, "test-model", 2), binding), embeddings ?? new());
    private static IndexedChunk Chunk(Guid doc, Guid rev, int id, string content, float x = 1, float y = 0) =>
        new(new(doc, rev, Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"), $"source line {id}", content), new float[] { x, y });
    private static RevisionIndexRequest Request(Guid doc, Guid rev, params IndexedChunk[] chunks) => new(doc, rev, "test-v1", chunks);

    [PostgresFact]
    public async Task ReplacementRemovesStaleChunksIsIdempotentAndPreservesOtherRevisions()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var other = Guid.NewGuid(); var store = Store();
        await store.ReplaceRevisionAsync(Request(doc, rev, Chunk(doc, rev, 1, "old pump"), Chunk(doc, rev, 2, "obsolete pump")), default);
        await store.ReplaceRevisionAsync(Request(doc, other, Chunk(doc, other, 3, "other pump")), default);
        var replacement = Request(doc, rev, Chunk(doc, rev, 4, "new pump"));
        await store.ReplaceRevisionAsync(replacement, default);
        await store.ReplaceRevisionAsync(replacement, default);
        var current = Assert.Single(await store.RetrieveAsync(new("pump", 10, doc, rev), RetrievalMode.Keyword, default));
        Assert.Equal(replacement.Chunks[0].Chunk.ChunkId, current.ChunkId);
        Assert.Equal("new pump", current.Snippet);
        Assert.Equal("source line 4", current.Locator);
        Assert.Equal(doc, current.DocumentId); Assert.Equal(rev, current.ManualRevisionId);
        Assert.Single(await store.RetrieveAsync(new("pump", 10, doc, other), RetrievalMode.Keyword, default));
    }

    [PostgresFact]
    public async Task KeywordDenseAndHybridRankGenuinelyAndRespectTopK()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var embeddings = new FakeEmbeddings(); var store = Store(embeddings: embeddings);
        await store.ReplaceRevisionAsync(Request(doc, rev, Chunk(doc, rev, 1, "pump pump pump"), Chunk(doc, rev, 2, "pump", .8f, .6f), Chunk(doc, rev, 3, "bearing seal")), default);
        var query = new RetrievalQuery("pump", 3, doc, rev);
        var keyword = await store.RetrieveAsync(query, RetrievalMode.Keyword, default);
        Assert.Equal(new[] { "pump pump pump", "pump" }, keyword.Select(r => r.Snippet));
        Assert.Equal(0, embeddings.Calls);
        var dense = await store.RetrieveAsync(query, RetrievalMode.Dense, default);
        Assert.Equal(new[] { "pump pump pump", "bearing seal", "pump" }, dense.Select(r => r.Snippet));
        Assert.Equal(1, dense[0].RelevanceScore, 6);
        Assert.Equal(.8, dense[2].RelevanceScore, 5);
        var hybrid = await store.RetrieveAsync(query, RetrievalMode.Hybrid, default);
        Assert.Equal(new[] { "pump pump pump", "pump", "bearing seal" }, hybrid.Select(r => r.Snippet));
        Assert.Equal(2d / 61, hybrid[0].RelevanceScore, 12);
        var repeated = await store.RetrieveAsync(query, RetrievalMode.Hybrid, default);
        Assert.Equal(hybrid, repeated);
        Assert.Single(await store.RetrieveAsync(new("pump", 1, doc, rev), RetrievalMode.Hybrid, default));
    }

    [PostgresFact]
    public async Task EmptyNoMatchAndSqlTextDoNotFabricateResultsOrExecuteSql()
    {
        var store = Store(); var doc = Guid.NewGuid(); var rev = Guid.NewGuid();
        Assert.Empty(await store.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Keyword, default));
        await store.ReplaceRevisionAsync(Request(doc, rev, Chunk(doc, rev, 1, "pump")), default);
        Assert.Empty(await store.RetrieveAsync(new("nonexistentword", 5, doc, rev), RetrievalMode.Keyword, default));
        Assert.Empty(await store.RetrieveAsync(new("'; DROP TABLE knowledge.chunks; --", 5, doc, rev), RetrievalMode.Keyword, default));
        Assert.Single(await store.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Keyword, default));
    }

    [PostgresFact]
    public async Task ProfileBindingAndDimensionMismatchFailWithoutReplacingKnowledge()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var store = Store();
        var initial = Request(doc, rev, Chunk(doc, rev, 1, "pump"));
        await store.ReplaceRevisionAsync(initial, default);
        var mismatch = Store(binding: "different-provider-same-dimensions");
        Assert.Equal(KnowledgeFailure.IncompatibleEmbeddingSpace, (await Assert.ThrowsAsync<KnowledgeStoreException>(() => mismatch.ReplaceRevisionAsync(initial, default))).Kind);
        await Assert.ThrowsAsync<KnowledgeStoreException>(() => mismatch.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Dense, default));
        var wrongVector = new RevisionIndexRequest(doc, rev, "test-v1", [new(initial.Chunks[0].Chunk, new float[] { 1, 0, 0 })]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReplaceRevisionAsync(wrongVector, default));
        Assert.Single(await store.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Keyword, default));
    }

    [PostgresFact]
    public async Task DifferentProfilesNeverReachTheSameVectorDistanceCalculation()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var profile = "three-" + Guid.NewGuid().ToString("N");
        var space = new EmbeddingSpace(profile, "test-model", 3);
        var three = new PostgresKnowledgeStore(database.Source, new(space, "three-dimensional-binding"), new FakeEmbeddings());
        await three.ReplaceRevisionAsync(new(doc, rev, profile, [new(new(doc, rev, Guid.NewGuid(), "line", "pump"), new float[] { 1, 0, 0 })]), default);
        var store = Store();
        await Assert.ThrowsAsync<KnowledgeStoreException>(() => store.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Keyword, default));
        // An unfiltered query may see other two-dimensional test data, but cannot evaluate
        // the distance operator against this three-dimensional profile.
        Assert.DoesNotContain(await store.RetrieveAsync(new("pump", 500), RetrievalMode.Dense, default), r => r.DocumentId == doc);
    }

    [PostgresFact]
    public async Task ConcurrentReplacementsPublishOneWholeBatchNotAUnion()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var store = Store();
        var one = Request(doc, rev, Chunk(doc, rev, 1, "pump alpha"), Chunk(doc, rev, 2, "pump alpha"));
        var two = Request(doc, rev, Chunk(doc, rev, 3, "pump beta"), Chunk(doc, rev, 4, "pump beta"));
        await Task.WhenAll(store.ReplaceRevisionAsync(one, default), store.ReplaceRevisionAsync(two, default));
        var results = await store.RetrieveAsync(new("pump", 10, doc, rev), RetrievalMode.Keyword, default);
        Assert.Equal(2, results.Count);
        Assert.Single(results.Select(r => r.Snippet).Distinct());
    }

    [PostgresFact]
    public async Task MidReplacementFailureRollsBackTheDeleteAndEarlierInserts()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var store = Store();
        await store.ReplaceRevisionAsync(Request(doc, rev, Chunk(doc, rev, 1, "pump original")), default);
        await Assert.ThrowsAsync<KnowledgeStoreException>(() => store.ReplaceRevisionAsync(Request(doc, rev,
            Chunk(doc, rev, 2, "pump replacement"), Chunk(doc, rev, 3, "invalid\0text")), default));
        Assert.Equal("pump original", Assert.Single(await store.RetrieveAsync(new("pump", 10, doc, rev), RetrievalMode.Keyword, default)).Snippet);
    }

    [PostgresFact]
    public async Task CancellationWhileWaitingForRevisionLockPreservesPriorSnapshot()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var store = Store();
        var initial = Request(doc, rev, Chunk(doc, rev, 1, "pump original"));
        await store.ReplaceRevisionAsync(initial, default);
        await using var connection = await database.Source.OpenConnectionAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT 1 FROM knowledge.revisions WHERE document_id=@doc AND revision_id=@rev FOR UPDATE", connection, transaction);
        command.Parameters.AddWithValue("doc", doc); command.Parameters.AddWithValue("rev", rev);
        await command.ExecuteNonQueryAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReplaceRevisionAsync(Request(doc, rev, Chunk(doc, rev, 2, "pump new")), cancellation.Token));
        Assert.Equal("pump original", Assert.Single(await store.RetrieveAsync(new("pump", 10, doc, rev), RetrievalMode.Keyword, default)).Snippet);
        await transaction.RollbackAsync();
    }

    [PostgresFact]
    public async Task EndToEndTextEmbeddingIndexAndGroundedRetrieval()
    {
        var doc = Guid.NewGuid(); var rev = Guid.NewGuid(); var store = Store();
        var ingestion = new ManualIngestionService(new TextDocumentProcessor(40, 5), new FakeEmbeddings(), store, new("test-v1", "test-model", 2));
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("Pump isolation procedure.\nCheck the pump seal before restart."));
        Assert.True(await ingestion.IngestAsync(new(doc, rev, "text/plain"), stream, default) > 0);
        var results = await store.RetrieveAsync(new("pump", 5, doc, rev), RetrievalMode.Hybrid, default);
        Assert.NotEmpty(results);
        Assert.All(results, r => { Assert.Equal(doc, r.DocumentId); Assert.Equal(rev, r.ManualRevisionId); Assert.StartsWith("text:lines", r.Locator); Assert.NotEmpty(r.Snippet); });
    }
}
