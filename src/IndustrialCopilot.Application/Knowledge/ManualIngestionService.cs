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
    private readonly IIngestionReports? reports;

    public ManualIngestionService(IDocumentProcessor processor, ILlmProvider embeddings, IKnowledgeIndex index,
        EmbeddingSpace space, int batchSize = 32, IIngestionReports? reports = null)
    {
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(embeddings);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(space);
        if (batchSize <= 0) throw new ArgumentOutOfRangeException(nameof(batchSize));
        this.reports = reports;
        this.processor = processor; this.embeddings = embeddings; this.index = index; this.space = space; this.batchSize = batchSize;
    }

    public async Task<int> IngestAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        await using var attempt = reports is null ? null : await reports.BeginAsync(request, cancellationToken).ConfigureAwait(false);
        try
        {
            var chunks = (processor is DocumentPipeline pipeline
                ? await pipeline.ProcessAsync(request, source, attempt, cancellationToken).ConfigureAwait(false)
                : await processor.ProcessAsync(request, source, cancellationToken).ConfigureAwait(false)).ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            if (chunks.Length == 0)
            {
                if (attempt is not null) await attempt.FailAsync(IngestionFailure.EmptyDocument, cancellationToken).ConfigureAwait(false);
                return 0;
            } // Empty extraction is never a delete operation.
            if (chunks.Any(c => c is null || c.DocumentId != request.DocumentId || c.ManualRevisionId != request.ManualRevisionId) ||
                chunks.Select(c => c.ChunkId).Distinct().Count() != chunks.Length)
                throw new InvalidOperationException("Processor returned invalid revision provenance.");
            if (attempt is not null) await attempt.AdvanceAsync(IngestionStage.Embedding, null, chunks.Length, cancellationToken).ConfigureAwait(false);
            var indexed = new List<IndexedChunk>();
            foreach (var batch in chunks.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var result = await embeddings.GenerateEmbeddingsAsync(new EmbeddingRequest(batch.Select(c => c.Content).ToArray()), cancellationToken).ConfigureAwait(false);
                space.Validate(result, batch.Length);
                for (var i = 0; i < batch.Length; i++) indexed.Add(new(batch[i], result.Vectors[i]));
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (attempt is not null) await attempt.AdvanceAsync(IngestionStage.Indexing, null, chunks.Length, cancellationToken).ConfigureAwait(false);
            await index.ReplaceRevisionAsync(new(request.DocumentId, request.ManualRevisionId, space.Profile, indexed, attempt?.Id), cancellationToken).ConfigureAwait(false);
            return indexed.Count;
        }
        catch (Exception error)
        {
            if (attempt is not null)
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                // Failure reporting cannot overwrite an atomically committed completion.
                try { await attempt.FailAsync(error switch
                {
                    DocumentInputException input => input.Failure,
                    NotSupportedException => IngestionFailure.UnsupportedFormat,
                    OperationCanceledException => IngestionFailure.Cancelled,
                    _ => IngestionFailure.StageFailed
                }, cleanup.Token).ConfigureAwait(false); }
                catch (Exception) { /* Store outage: inspection reports Interrupted after this session closes. */ }
            }
            throw;
        }
    }
}
