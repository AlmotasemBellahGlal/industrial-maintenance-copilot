using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Knowledge;

/// <summary>Explicit Extract / Clean / Chunk composition; adapters cannot index partial extraction.</summary>
public sealed class DocumentPipeline(IDocumentExtractor extractor, IDocumentCleaner cleaner, IDocumentChunker chunker) : IDocumentProcessor
{
    public Task<IReadOnlyList<DocumentChunk>> ProcessAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken) =>
        ProcessAsync(request, source, null, cancellationToken);

    public async Task<IReadOnlyList<DocumentChunk>> ProcessAsync(DocumentProcessingRequest request, Stream source,
        IIngestionAttempt? attempt, CancellationToken token)
    {
        var extracted = await extractor.ExtractAsync(request, source, token).ConfigureAwait(false);
        if (attempt is not null) await attempt.AdvanceAsync(IngestionStage.Cleaning, extracted.PageCount, null, token).ConfigureAwait(false);
        var cleaned = cleaner.Clean(extracted, token);
        if (attempt is not null) await attempt.AdvanceAsync(IngestionStage.Chunking, extracted.PageCount, null, token).ConfigureAwait(false);
        return chunker.Chunk(request, cleaned, token);
    }
}
