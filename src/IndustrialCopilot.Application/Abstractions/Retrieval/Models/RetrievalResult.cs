namespace IndustrialCopilot.Application.Abstractions.Retrieval.Models;

public sealed record RetrievalResult
{
    // DocumentId corresponds to Domain Manual.Id.
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }
    // Stable application chunk identity, independent of the vector-store record key.
    public Guid ChunkId { get; }
    public string Locator { get; }
    public string Snippet { get; }
    // Higher means more relevant within one result set; not comparable across providers or modes.
    public double RelevanceScore { get; }

    public RetrievalResult(Guid documentId, Guid manualRevisionId, Guid chunkId,
        string locator, string snippet, double relevanceScore)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        if (chunkId == Guid.Empty) throw new ArgumentException("Chunk identity is required.", nameof(chunkId));
        ArgumentException.ThrowIfNullOrWhiteSpace(locator);
        ArgumentException.ThrowIfNullOrWhiteSpace(snippet);
        if (!double.IsFinite(relevanceScore)) throw new ArgumentOutOfRangeException(nameof(relevanceScore));
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
        ChunkId = chunkId;
        Locator = locator;
        Snippet = snippet;
        RelevanceScore = relevanceScore;
    }
}
