namespace IndustrialCopilot.Application.Abstractions.AI.Models;

// Vectors correspond to request inputs in the same order.
public sealed record EmbeddingResult
{
    public IReadOnlyList<IReadOnlyList<float>> Vectors { get; }
    public string? Model { get; }

    public EmbeddingResult(IReadOnlyList<IReadOnlyList<float>> vectors, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        var snapshot = new IReadOnlyList<float>[vectors.Count];
        for (var index = 0; index < vectors.Count; index++)
        {
            var vector = vectors[index];
            if (vector is null) throw new ArgumentException("Vectors cannot contain null.", nameof(vectors));
            snapshot[index] = Array.AsReadOnly(vector.ToArray());
        }
        Vectors = Array.AsReadOnly(snapshot);
        Model = model;
    }
}
