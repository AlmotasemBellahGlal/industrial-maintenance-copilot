namespace IndustrialCopilot.Application.Abstractions.Agents.Evidence;

// Provenance supplied by Application; construction does not establish authenticity or truth.
public sealed record GroundedEvidence
{
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }
    public Guid ChunkId { get; }
    public string Locator { get; }
    public string Snippet { get; }

    public GroundedEvidence(Guid documentId, Guid manualRevisionId, Guid chunkId, string locator, string snippet)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        if (chunkId == Guid.Empty) throw new ArgumentException("Chunk identity is required.", nameof(chunkId));
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet);
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
        ChunkId = chunkId;
        Locator = locator;
        Snippet = snippet;
    }
}
