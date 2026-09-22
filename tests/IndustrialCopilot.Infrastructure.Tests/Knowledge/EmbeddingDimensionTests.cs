using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Corpus;

namespace IndustrialCopilot.Infrastructure.Tests.Knowledge;

/// <summary>
/// Contract tests for the CorpusEmbeddings deterministic lexical provider.
/// Verifies dimension count, norm, determinism, and that equipment-family-specific
/// queries produce higher similarity to their matching corpus content than to
/// unrelated equipment families.
/// </summary>
public class EmbeddingDimensionTests
{
    private static IReadOnlyList<float> Embed(string text)
    {
        var provider = new CorpusEmbeddings();
        var result = provider.GenerateEmbeddingsAsync(new EmbeddingRequest([text]), default).GetAwaiter().GetResult();
        return result.Vectors[0];
    }

    private static double CosineSimilarity(IReadOnlyList<float> a, IReadOnlyList<float> b)
    {
        if (a.Count != b.Count) throw new ArgumentException("Mismatched dimensions.");
        double dot = 0, normA = 0, normB = 0;
        for (var i = 0; i < a.Count; i++) { dot += a[i] * b[i]; normA += a[i] * a[i]; normB += b[i] * b[i]; }
        return normA == 0 || normB == 0 ? 0 : dot / (Math.Sqrt(normA) * Math.Sqrt(normB));
    }

    [Fact]
    public void EmbeddingHas256Dimensions()
    {
        var v = Embed("pump seal leakage");
        Assert.Equal(256, v.Count);
    }

    [Fact]
    public void EmbeddingIsNonZero()
    {
        var v = Embed("pump seal leakage");
        Assert.True(v.Any(x => x > 0), "Embedding must be non-zero.");
        var norm = Math.Sqrt(v.Sum(x => (double)x * x));
        Assert.True(norm > 0, "Embedding norm must be positive.");
    }

    [Fact]
    public void EmbeddingIsDeterministic()
    {
        var a = Embed("pump seal leakage");
        var b = Embed("pump seal leakage");
        Assert.Equal(a, b);
    }

    [Fact]
    public async Task ModelTagIsPreserved()
    {
        var provider = new CorpusEmbeddings();
        var result = await provider.GenerateEmbeddingsAsync(new EmbeddingRequest(["test"]), default);
        Assert.Equal("synthetic-lexical-v1", result.Model);
    }

    [Fact]
    public async Task BatchEmbeddingsAreConsistentWithSingle()
    {
        var provider = new CorpusEmbeddings();
        var batch = await provider.GenerateEmbeddingsAsync(new EmbeddingRequest(["pump seal leakage", "motor overheating"]), default);
        var single1 = Embed("pump seal leakage");
        var single2 = Embed("motor overheating");
        Assert.Equal(single1, batch.Vectors[0]);
        Assert.Equal(single2, batch.Vectors[1]);
    }

    [Fact]
    public void PumpQueryIsMoreSimilarToPumpContentThanMotorContent()
    {
        // The 256-dim bucket space should discriminate equipment-family-specific vocabulary
        // better than the 32-dim space (8x fewer collisions).
        var pumpQuery = Embed("pump seal leakage observations");
        var pumpContent = Embed("seal housing drip tray isolated seal housing pump");
        var motorContent = Embed("motor terminal enclosure overheating casing temperature unloaded baseline");

        var simPump = CosineSimilarity(pumpQuery, pumpContent);
        var simMotor = CosineSimilarity(pumpQuery, motorContent);

        Assert.True(simPump > simMotor,
            $"Pump query should be more similar to pump content ({simPump:F4}) than motor content ({simMotor:F4}).");
    }

    [Fact]
    public void MotorQueryIsMoreSimilarToMotorContentThanPumpContent()
    {
        var motorQuery = Embed("motor overheating casing temperature");
        var motorContent = Embed("terminal enclosure overheating casing temperature unloaded baseline motor");
        var pumpContent = Embed("seal housing drip tray isolated seal housing pump leakage");

        var simMotor = CosineSimilarity(motorQuery, motorContent);
        var simPump = CosineSimilarity(motorQuery, pumpContent);

        Assert.True(simMotor > simPump,
            $"Motor query should be more similar to motor content ({simMotor:F4}) than pump content ({simPump:F4}).");
    }

    [Fact]
    public void IsolationQueryMatchesIsolationContentAcrossFamilies()
    {
        // Isolation/hazard queries should be similar to isolation content regardless of equipment family.
        var isoQuery = Embed("isolation evidence required before touching");
        var pumpIso = Embed("before touching the mechanical seal identify all stored and supplied energy electrical isolation verification absence of hazardous energy pump");
        var motorIso = Embed("before touching the terminal enclosure identify all stored and supplied energy electrical isolation verification absence of hazardous energy motor");

        var simPump = CosineSimilarity(isoQuery, pumpIso);
        var simMotor = CosineSimilarity(isoQuery, motorIso);

        // Both should be reasonably similar (same isolation language)
        Assert.True(simPump > 0.1, $"Isolation query should have positive similarity to pump isolation ({simPump:F4}).");
        Assert.True(simMotor > 0.1, $"Isolation query should have positive similarity to motor isolation ({simMotor:F4}).");
    }

    [Fact]
    public void EmbeddingBaselineIsStableAcrossTokenizationBoundaries()
    {
        // Splitting on different whitespace/punctuation should not affect content of same word.
        var withPeriod = Embed("pump. seal: leakage;");
        var withSpaces = Embed("pump seal leakage");
        // Same words, different punctuation — buckets should be the same because delimiters are excluded.
        Assert.Equal(withPeriod, withSpaces);
    }
}
