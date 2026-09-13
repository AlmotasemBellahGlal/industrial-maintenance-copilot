using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;

namespace IndustrialCopilot.Application.Knowledge;

/// <summary>Publishes one complete revision only after all processing/embedding succeeds.</summary>
public sealed class ManualIngestionService
{
    private readonly IDocumentProcessor processor;
    private readonly ILlmProvider embeddings;
    private readonly IKnowledgeIndex index;
    private readonly EmbeddingSpace space;
    private readonly int batchSize;

    public ManualIngestionService(IDocumentProcessor processor, ILlmProvider embeddings, IKnowledgeIndex index,
        EmbeddingSpace space, int batchSize = 32)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(space);
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        this.processor = processor; this.embeddings = embeddings; this.index = index; this.space = space; this.batchSize = batchSize;
    }

    public async Task<int> IngestAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var chunks = (await processor.ProcessAsync(request, source, cancellationToken).ConfigureAwait(false)).ToArray();
        cancellationToken.ThrowIfCancellationRequested();
        if (chunks.Length == 0) return 0; // Empty extraction is never a delete operation.
        if (chunks.Any(c => c is null || c.DocumentId != request.DocumentId || c.ManualRevisionId != request.ManualRevisionId) ||
            chunks.Select(c => c.ChunkId).Distinct().Count() != chunks.Length)
            throw new InvalidOperationException("Processor returned invalid revision provenance.");
        var indexed = new List<IndexedChunk>();
        foreach (var batch in chunks.Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await embeddings.GenerateEmbeddingsAsync(new EmbeddingRequest(batch.Select(c => c.Content).ToArray()), cancellationToken).ConfigureAwait(false);
            space.Validate(result, batch.Length);
            for (var i = 0; i < batch.Length; i++) indexed.Add(new(batch[i], result.Vectors[i]));
        }
        cancellationToken.ThrowIfCancellationRequested();
        await index.ReplaceRevisionAsync(new(request.DocumentId, request.ManualRevisionId, space.Profile, indexed), cancellationToken).ConfigureAwait(false);
        return indexed.Count;
    }
}
