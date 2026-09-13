using IndustrialCopilot.Application.Knowledge;

namespace IndustrialCopilot.Infrastructure.Knowledge;

public sealed class KnowledgeStoreOptions
{
    public EmbeddingSpace Space { get; }
    public string Binding { get; }
    public int HybridCandidates { get; }
    public double MinimumCosineSimilarity { get; }

    public KnowledgeStoreOptions(EmbeddingSpace space, string binding, int hybridCandidates = 100, double minimumCosineSimilarity = 0)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentException.ThrowIfNullOrWhiteSpace(binding);
        if (space.Dimensions > 16000 || hybridCandidates <= 0 || !double.IsFinite(minimumCosineSimilarity) || minimumCosineSimilarity is < -1 or > 1)
            throw new ArgumentException("Invalid knowledge search limits.");
        Space = space; Binding = binding; HybridCandidates = hybridCandidates; MinimumCosineSimilarity = minimumCosineSimilarity;
    }
}
