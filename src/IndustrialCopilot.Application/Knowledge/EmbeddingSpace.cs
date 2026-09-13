using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Application.Knowledge;

/// <summary>The application-owned profile is distinct from the expected embedding model identifier.
/// Configuring equal dimensions does not establish compatibility between spaces.</summary>
public sealed class EmbeddingSpace
{
    public string Profile { get; }
    public string Model { get; }
    public int Dimensions { get; }

    public EmbeddingSpace(string profile, string model, int dimensions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (dimensions <= 0) throw new ArgumentOutOfRangeException(nameof(dimensions));
        Profile = profile;
        Model = model;
        Dimensions = dimensions;
    }

    public void Validate(EmbeddingResult result, int expectedCount)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.Model != Model || result.Vectors.Count != expectedCount)
            throw new InvalidOperationException("Embedding output does not match the configured space or batch.");
        foreach (var vector in result.Vectors) ValidateVector(vector);
    }

    public void ValidateVector(IReadOnlyList<float> vector)
    {
        if (vector.Count != Dimensions || vector.Any(v => !float.IsFinite(v)) || !vector.Any(v => v != 0))
            throw new InvalidOperationException("Embedding dimension or values are incompatible with cosine retrieval.");
    }
}
