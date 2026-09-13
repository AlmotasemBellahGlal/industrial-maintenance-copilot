using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class SseReaderTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r")]
    [InlineData("\r\n")]
    public async Task HandlesEverySseLineEndingAcrossReadBoundaries(string newline)
    {
        using var stream = new FragmentedStream("data: one" + newline + "data: two" + newline + newline, 1);
        var events = new List<string>();
        await foreach (var item in SseReader.ReadAsync(stream, default)) events.Add(item);
        Assert.Equal("one\ntwo", Assert.Single(events));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BoundsUnterminatedLinesAndMultilineEvents(bool manyLines)
    {
        var text = manyLines ? string.Concat(Enumerable.Repeat("data: payload\n", 100000)) : "data:" + new string('x', SseReader.MaximumEventCharacters);
        using var stream = new FragmentedStream(text, 4096);
        var error = await Assert.ThrowsAsync<LlmProviderException>(async () =>
        {
            await foreach (var item in SseReader.ReadAsync(stream, default)) { }
        });
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
    }
}
