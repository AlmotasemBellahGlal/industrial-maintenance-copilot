using System.Runtime.CompilerServices;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class ConfiguredProviderTests
{
    private sealed class Stub : ILlmProvider
    {
        internal int Completions, Tools, Streams, Embeddings;
        internal Exception? Failure;
        internal bool EmitFirst;
        internal Action? BeforeFailure;
        internal IReadOnlyList<ToolDefinition>? SeenTools;
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken)
        {
            Completions++;
            BeforeFailure?.Invoke();
            if (Failure is not null) throw Failure;
            return Task.FromResult(new CompletionResponse("ok", new(1, 1, 2)));
        }
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
        {
            Tools++;
            SeenTools = tools;
            BeforeFailure?.Invoke();
            if (Failure is not null) throw Failure;
            return Task.FromResult(new ToolCompletionResponse("ok", [], new(1, 1, 2)));
        }
        public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            Streams++;
            await Task.CompletedTask;
            if (EmitFirst) yield return new("", false); // Even an empty emitted chunk forbids fallback.
            BeforeFailure?.Invoke();
            if (Failure is not null) throw Failure;
            yield return new("ok", true);
        }
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            Embeddings++;
            if (Failure is not null) throw Failure;
            return Task.FromResult(new EmbeddingResult([new float[] { 1 }]));
        }
    }

    [Theory]
    [InlineData(LlmProviderFailureKind.Timeout)]
    [InlineData(LlmProviderFailureKind.TransientTransport)]
    [InlineData(LlmProviderFailureKind.TemporaryRateLimit)]
    [InlineData(LlmProviderFailureKind.Unavailable)]
    public async Task OnlyOneFallbackAttemptForEligibleCompletionFailure(LlmProviderFailureKind kind)
    {
        var primary = new Stub { Failure = new LlmProviderException(kind) };
        var fallback = new Stub();
        var provider = new ConfiguredLlmProvider(primary, fallback, primary);
        Assert.Equal("ok", (await provider.CompleteAsync(Fixtures.Query, default)).Content);
        Assert.Equal(1, primary.Completions);
        Assert.Equal(1, fallback.Completions);
    }

    [Theory]
    [InlineData(LlmProviderFailureKind.Authentication)]
    [InlineData(LlmProviderFailureKind.InvalidRequest)]
    [InlineData(LlmProviderFailureKind.InvalidResponse)]
    [InlineData(LlmProviderFailureKind.Refused)]
    [InlineData(LlmProviderFailureKind.QuotaExceeded)]
    [InlineData(LlmProviderFailureKind.RateLimited)]
    [InlineData(LlmProviderFailureKind.Transport)]
    [InlineData(LlmProviderFailureKind.UnsupportedResponse)]
    [InlineData(LlmProviderFailureKind.HttpFailure)]
    public async Task NonTransientFailureNeverInvokesFallback(LlmProviderFailureKind kind)
    {
        var error = new LlmProviderException(kind);
        var fallback = new Stub();
        var provider = new ConfiguredLlmProvider(new Stub { Failure = error }, fallback, fallback);
        Assert.Same(error, await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default)));
        Assert.Equal(0, fallback.Completions);
    }

    [Fact]
    public async Task SuccessUsesPrimaryAndDisabledFallbackPropagatesFailure()
    {
        var primary = new Stub();
        var fallback = new Stub();
        await new ConfiguredLlmProvider(primary, fallback, primary).CompleteAsync(Fixtures.Query, default);
        Assert.Equal(1, primary.Completions);
        Assert.Equal(0, fallback.Completions);
        primary.Failure = new LlmProviderException(LlmProviderFailureKind.Timeout);
        await Assert.ThrowsAsync<LlmProviderException>(() => new ConfiguredLlmProvider(primary, null, primary).CompleteAsync(Fixtures.Query, default));
        Assert.Equal(2, primary.Completions);
    }

    [Fact]
    public async Task BothProvidersFailWithoutLooping()
    {
        var primary = new Stub { Failure = new LlmProviderException(LlmProviderFailureKind.Timeout) };
        var fallback = new Stub { Failure = new LlmProviderException(LlmProviderFailureKind.Unavailable) };
        var provider = new ConfiguredLlmProvider(primary, fallback, primary);
        Assert.Same(fallback.Failure, await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default)));
        Assert.Equal(1, primary.Completions);
        Assert.Equal(1, fallback.Completions);
    }

    [Fact]
    public async Task ToolFallbackKeepsOriginalAllowListSnapshot()
    {
        var tools = new List<ToolDefinition> { new("lookup", "read", System.Text.Json.JsonSerializer.SerializeToElement(new { type = "object" })) };
        var primary = new Stub { Failure = new LlmProviderException(LlmProviderFailureKind.Timeout), BeforeFailure = () => tools.Clear() };
        var fallback = new Stub();
        var result = await new ConfiguredLlmProvider(primary, fallback, primary).CompleteWithToolsAsync(Fixtures.Query, tools, default);
        Assert.Equal("ok", result.Content);
        Assert.Single(fallback.SeenTools!);
        Assert.Same(primary.SeenTools, fallback.SeenTools);
        Assert.Equal(1, fallback.Tools);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeFallbackPreventsTheSecondAttempt(bool streaming)
    {
        using var cancellation = new CancellationTokenSource();
        var primary = new Stub { Failure = new LlmProviderException(LlmProviderFailureKind.Timeout), BeforeFailure = cancellation.Cancel };
        var fallback = new Stub();
        var provider = new ConfiguredLlmProvider(primary, fallback, primary);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            if (streaming) await Collect(provider, cancellation.Token);
            else await provider.CompleteAsync(Fixtures.Query, cancellation.Token);
        });
        Assert.Equal(0, fallback.Completions + fallback.Streams);
    }

    [Fact]
    public async Task CallerCancellationAndPreCancelledRequestsNeverFallback()
    {
        var fallback = new Stub();
        var primary = new Stub { Failure = new OperationCanceledException() };
        var provider = new ConfiguredLlmProvider(primary, fallback, primary);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Fixtures.Query, default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CompleteAsync(Fixtures.Query, cancellation.Token));
        Assert.Equal(1, primary.Completions);
        Assert.Equal(0, fallback.Completions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StreamingFallbackOnlyBeforeAnyChunkIsExposed(bool emitFirst)
    {
        var primary = new Stub { Failure = new LlmProviderException(LlmProviderFailureKind.Timeout), EmitFirst = emitFirst };
        var fallback = new Stub();
        var provider = new ConfiguredLlmProvider(primary, fallback, primary);
        if (emitFirst) await Assert.ThrowsAsync<LlmProviderException>(() => Collect(provider));
        else Assert.Equal("ok", (await Collect(provider)).Single().ContentDelta);
        Assert.Equal(emitFirst ? 0 : 1, fallback.Streams);
        Assert.Equal(1, primary.Streams);
    }

    [Fact]
    public async Task EmbeddingProviderIsIndependentAndNeverFallsBack()
    {
        var primary = new Stub();
        var selected = new Stub();
        var provider = new ConfiguredLlmProvider(primary, selected, selected);
        await provider.GenerateEmbeddingsAsync(new(["text"]), default);
        Assert.Equal(0, primary.Embeddings);
        Assert.Equal(1, selected.Embeddings);
        selected.Failure = new LlmProviderException(LlmProviderFailureKind.Timeout);
        await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateEmbeddingsAsync(new(["text"]), default));
        Assert.Equal(0, primary.Embeddings);
        Assert.Equal(2, selected.Embeddings);
    }

    private static async Task<List<StreamingChunk>> Collect(ILlmProvider provider, CancellationToken cancellationToken = default)
    {
        var result = new List<StreamingChunk>();
        await foreach (var chunk in provider.StreamAsync(Fixtures.Query, cancellationToken)) result.Add(chunk);
        return result;
    }
}
