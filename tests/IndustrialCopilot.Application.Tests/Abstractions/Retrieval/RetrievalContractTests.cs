using IndustrialCopilot.Application.Abstractions.Retrieval.Models;

namespace IndustrialCopilot.Application.Tests.Abstractions.Retrieval;

public class RetrievalContractTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingQueryText(string? text) =>
        Assert.ThrowsAny<ArgumentException>(() => new RetrievalQuery(text!, 5));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonpositiveTopK(int topK) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new RetrievalQuery("pump vibration", topK));

    [Fact]
    public void RejectsEmptyFilterIdentities()
    {
        Assert.Throws<ArgumentException>(() => new RetrievalQuery("pump vibration", 5, Guid.Empty));
        Assert.Throws<ArgumentException>(() => new RetrievalQuery("pump vibration", 5, manualRevisionId: Guid.Empty));
    }

    [Fact]
    public void AllowsUnfilteredDocumentFilteredAndRevisionFilteredQueries()
    {
        _ = new RetrievalQuery("pump vibration", 5);
        _ = new RetrievalQuery("pump vibration", 5, Guid.NewGuid());
        _ = new RetrievalQuery("pump vibration", 5, manualRevisionId: Guid.NewGuid());
        _ = new RetrievalQuery("pump vibration", 5, Guid.NewGuid(), Guid.NewGuid());
    }

    [Fact]
    public void RejectsMissingResultIdentities()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new RetrievalResult(Guid.Empty, id, id, "Page 3", "Inspect coupling", 1));
        Assert.Throws<ArgumentException>(() => new RetrievalResult(id, Guid.Empty, id, "Page 3", "Inspect coupling", 1));
        Assert.Throws<ArgumentException>(() => new RetrievalResult(id, id, Guid.Empty, "Page 3", "Inspect coupling", 1));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingCitationText(string? text)
    {
        var id = Guid.NewGuid();
        Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult(id, id, id, text!, "Inspect coupling", 1));
        Assert.ThrowsAny<ArgumentException>(() => new RetrievalResult(id, id, id, "Page 3", text!, 1));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void RejectsNonfiniteScores(double score) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Result(score));

    [Theory]
    [InlineData(-20.0)]
    [InlineData(0.0)]
    [InlineData(42.0)]
    public void AllowsFiniteScoresOutsideNormalizedRanges(double score)
    {
        _ = Result(score);
    }

    private static RetrievalResult Result(double score) =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Page 3, section 2", "Inspect coupling", score);
}
