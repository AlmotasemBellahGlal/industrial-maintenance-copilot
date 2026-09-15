using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Knowledge;
namespace IndustrialCopilot.Infrastructure.Knowledge;

/// <summary>Compatibility facade over the shared stages. Original text-v1 IDs and locators are preserved.</summary>
public sealed class TextDocumentProcessor : IDocumentProcessor
{
    private readonly DocumentPipeline pipeline;
    public TextDocumentProcessor(int chunkSize = 1200, int overlap = 200, int maxCharacters = 4_000_000)
    {
        if (maxCharacters < chunkSize) throw new ArgumentException("Invalid text limits.");
        pipeline = new(new Utf8DocumentExtractor(maxCharacters), new DocumentCleaner(), new DeterministicDocumentChunker(chunkSize, overlap));
    }
    public Task<IReadOnlyList<DocumentChunk>> ProcessAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken) =>
        pipeline.ProcessAsync(request, source, cancellationToken);
}
