using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class OpenAiStreamingTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(65536)]
    public async Task ReadsFragmentedUtf8AndCoalescedEventsWithFinalUsage(int fragmentSize)
    {
        var stream = new FragmentedStream(": keepalive\r\n\r\n" + Fixtures.Delta("Check ") + Fixtures.Delta("مضخة") + Fixtures.Finish + Fixtures.Usage + Fixtures.Done, fragmentSize);
        var handler = new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream)));
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var chunks = await Collect(provider);
        Assert.Equal(new[] { "Check ", "مضخة", "" }, chunks.Select(c => c.ContentDelta));
        Assert.All(chunks.Take(2), c => { Assert.False(c.IsCompleted); Assert.Null(c.Usage); });
        Assert.True(chunks[^1].IsCompleted);
        Assert.Equal(new TokenUsage(3, 2, 5), chunks[^1].Usage);
        Assert.True(handler.Body!["stream"]!.GetValue<bool>());
        Assert.True(handler.Body["stream_options"]!["include_usage"]!.GetValue<bool>());
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task SupportsMultilineSseDataAndOptionalUsage()
    {
        var stream = new FragmentedStream("event: message\r\ndata: {\"choices\":\r\ndata: [{\"index\":0,\"delta\":{\"content\":\"text\"}}]}\r\n\r\n" + Fixtures.Finish + Fixtures.Done);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        var chunks = await Collect(provider);
        Assert.Equal("text", chunks[0].ContentDelta);
        Assert.True(chunks[^1].IsCompleted);
        Assert.Null(chunks[^1].Usage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(Fixtures.Finish)]
    [InlineData(Fixtures.Done)]
    [InlineData("data: {broken}\n\n")]
    [InlineData("data: [DONE]")]
    public async Task InterruptedOrInvalidProtocolNeverProducesSuccessfulTerminalChunk(string suffix)
    {
        var stream = new FragmentedStream(Fixtures.Delta("partial") + suffix);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        var chunks = new List<StreamingChunk>();
        var error = await Assert.ThrowsAsync<LlmProviderException>(async () =>
        {
            await foreach (var chunk in provider.StreamAsync(Fixtures.Query, default)) chunks.Add(chunk);
        });
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
        Assert.Single(chunks);
        Assert.False(chunks[0].IsCompleted);
        Assert.True(stream.WasDisposed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringReadHonorsMethodAndEnumerationTokens(bool enumerationToken)
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new FragmentedStream(Fixtures.Delta("partial"), blockAtEnd: true);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        await using var iterator = provider.StreamAsync(Fixtures.Query, enumerationToken ? default : cancellation.Token)
            .GetAsyncEnumerator(enumerationToken ? cancellation.Token : default);
        Assert.True(await iterator.MoveNextAsync());
        var pending = iterator.MoveNextAsync().AsTask();
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task StreamingDeadlineIncludesBodyReadsAndIsNotCallerCancellation()
    {
        var stream = new FragmentedStream(Fixtures.Delta("partial"), blockAtEnd: true);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(streamTimeout: TimeSpan.FromMilliseconds(150)), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => Collect(provider));
        Assert.Equal(LlmProviderFailureKind.Timeout, error.Kind);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task ConsumerEarlyDisposalClosesResponseStream()
    {
        var stream = new FragmentedStream(Fixtures.Delta("partial"), blockAtEnd: true);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        await foreach (var chunk in provider.StreamAsync(Fixtures.Query, default)) break;
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task StreamingCannotDropToolCalls()
    {
        var stream = new FragmentedStream("data: {\"choices\":[{\"index\":0,\"delta\":{\"tool_calls\":[{}]}}]}\n\n");
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => Collect(provider));
        Assert.Equal(LlmProviderFailureKind.UnsupportedResponse, error.Kind);
    }

    private static async Task<List<StreamingChunk>> Collect(OpenAiLlmProvider provider)
    {
        var chunks = new List<StreamingChunk>();
        await foreach (var chunk in provider.StreamAsync(Fixtures.Query, default)) chunks.Add(chunk);
        return chunks;
    }
}
