using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Abstractions.Documents;

public interface IDocumentProcessor
{
    /// <summary>Extracts and chunks a manual revision without indexing it.</summary>
    /// <remarks>
    /// Request and source must be non-null, and source must be readable.
    /// Reads from the current position without requiring seeking; never disposes the caller's stream.
    /// Returns a non-null, read-only snapshot in source order with no null chunks.
    /// Chunk IDs are unique and stable for the same revision and processing output.
    /// Every chunk's document and revision identities match the request.
    /// Unsupported or corrupt extraction fails instead of returning partial content.
    /// Empty output must not implicitly erase previously indexed knowledge.
    /// </remarks>
    Task<IReadOnlyList<DocumentChunk>> ProcessAsync(
        DocumentProcessingRequest request,
        Stream source,
        CancellationToken cancellationToken);
}
