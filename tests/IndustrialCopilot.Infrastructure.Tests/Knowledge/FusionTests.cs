using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Infrastructure.Knowledge;

namespace IndustrialCopilot.Infrastructure.Tests.Knowledge;

public class FusionTests
{
    [Fact]
    public void UsesReciprocalRanksDeduplicatesAndPreservesCitations()
    {
        var document = Guid.NewGuid(); var revision = Guid.NewGuid();
        RetrievalResult Result(int id, double score) => new(document, revision, Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"), "line 7", $"source {id}", score);
        var one = Result(1, 900); var two = Result(2, 0.4); var three = Result(3, 0.3);
        var result = ReciprocalRankFusion.Combine([one, two], [three, two], 2);
        Assert.Equal(two.ChunkId, result[0].ChunkId);
        Assert.Equal(2d / 62, result[0].RelevanceScore, 12);
        Assert.Equal(two.Locator, result[0].Locator);
        Assert.Equal(two.Snippet, result[0].Snippet);
        Assert.Equal(document, result[0].DocumentId);
        Assert.Equal(revision, result[0].ManualRevisionId);
        Assert.Equal(one.ChunkId, result[1].ChunkId); // deterministic UUID tie-break
        Assert.Empty(ReciprocalRankFusion.Combine([], [], 3));
    }
}
