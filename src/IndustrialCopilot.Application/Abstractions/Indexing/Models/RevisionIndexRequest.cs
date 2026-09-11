namespace IndustrialCopilot.Application.Abstractions.Indexing.Models;

public sealed record RevisionIndexRequest
{
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }
    // Application/configuration-owned embedding-space identifier, not a provider model name.
    public string EmbeddingProfile { get; }
    public IReadOnlyList<IndexedChunk> Chunks { get; }

    public RevisionIndexRequest(Guid documentId, Guid manualRevisionId, string embeddingProfile,
        IReadOnlyList<IndexedChunk> chunks)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingProfile);
        ArgumentNullException.ThrowIfNull(chunks);
        var snapshot = chunks.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(chunk => chunk is null))
            throw new ArgumentException("A nonempty batch with no null chunks is required.", nameof(chunks));
        if (snapshot.Select(chunk => chunk.Chunk.ChunkId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Chunk identities must be unique.", nameof(chunks));
        if (snapshot.Any(chunk => chunk.Chunk.DocumentId != documentId || chunk.Chunk.ManualRevisionId != manualRevisionId))
            throw new ArgumentException("Chunks must belong to the requested document revision.", nameof(chunks));
        if (snapshot.Any(chunk => chunk.Vector.Count != snapshot[0].Vector.Count))
            throw new ArgumentException("Vector dimensions must be consistent.", nameof(chunks));
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
        EmbeddingProfile = embeddingProfile;
        Chunks = Array.AsReadOnly(snapshot);
    }
}
