namespace IndustrialCopilot.Application.Abstractions.Documents.Models;

public sealed record DocumentChunk
{
    // DocumentId corresponds to Domain Manual.Id.
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }
    public Guid ChunkId { get; }
    public string Locator { get; }
    public string Content { get; }
    public DocumentMetadata? Metadata { get; }
    public int? Page { get; }
    public string? Section { get; }

    public DocumentChunk(Guid documentId, Guid manualRevisionId, Guid chunkId, string locator, string content, DocumentMetadata? metadata = null, int? page = null, string? section = null)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        if (chunkId == Guid.Empty) throw new ArgumentException("Chunk identity is required.", nameof(chunkId));
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
        ChunkId = chunkId;
        Locator = locator;
        if (section is not null) ArgumentException.ThrowIfNullOrWhiteSpace(section);
        if (page is <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        Content = content;
        Metadata = metadata; Page = page; Section = section;
    }
}
