using IndustrialCopilot.Application.Abstractions.Retrieval.Models;

namespace IndustrialCopilot.Infrastructure.Knowledge;

internal static class ReciprocalRankFusion
{
    internal static IReadOnlyList<RetrievalResult> Combine(IReadOnlyList<RetrievalResult> keyword, IReadOnlyList<RetrievalResult> dense, int topK)
    {
        var scores = new Dictionary<(Guid, Guid, Guid), (RetrievalResult Result, double Score)>();
        foreach (var ranking in new[] { keyword, dense })
            for (var i = 0; i < ranking.Count; i++)
            {
                var result = ranking[i];
                var key = (result.DocumentId, result.ManualRevisionId, result.ChunkId);
                var score = 1d / (60 + i + 1);
                scores[key] = (result, score + (scores.TryGetValue(key, out var existing) ? existing.Score : 0));
            }
        return Array.AsReadOnly(scores.Values.OrderByDescending(v => v.Score)
            .ThenBy(v => v.Result.DocumentId).ThenBy(v => v.Result.ManualRevisionId).ThenBy(v => v.Result.ChunkId).Take(topK)
            .Select(v => new RetrievalResult(v.Result.DocumentId, v.Result.ManualRevisionId, v.Result.ChunkId,
                v.Result.Locator, v.Result.Snippet, v.Score)).ToArray());
    }
}
