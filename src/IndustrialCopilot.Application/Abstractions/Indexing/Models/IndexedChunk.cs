using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Abstractions.Indexing.Models;

public sealed record IndexedChunk
{
    public DocumentChunk Chunk { get; }
    public IReadOnlyList<float> Vector { get; }

    public IndexedChunk(DocumentChunk chunk, IReadOnlyList<float> vector)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(vector);
        var snapshot = vector.ToArray();
        if (snapshot.Length == 0) throw new ArgumentException("Vector must not be empty.", nameof(vector));
        if (snapshot.Any(value => !float.IsFinite(value)))
            throw new ArgumentException("Vector values must be finite.", nameof(vector));
        Chunk = chunk;
        Vector = Array.AsReadOnly(snapshot);
    }
}
