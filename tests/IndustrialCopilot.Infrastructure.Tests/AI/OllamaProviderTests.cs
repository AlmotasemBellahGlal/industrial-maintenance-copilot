using System.Net;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class OllamaProviderTests
{
    internal static OllamaOptions Options => new(new Uri("http://localhost:11434/"), "llama3.1:8b", "embeddinggemma",
        TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));

    [Fact]
    public async Task MapsCompatibleChatOptionsRolesAndUsageWithoutHostedCredentials()
    {
        var handler = Fixtures.Handler(Fixtures.Completion);
        using var provider = new OllamaLlmProvider(Options, handler);
        var response = await provider.CompleteAsync(new CompletionRequest([new(LlmRole.System, "Ground answers"), new(LlmRole.User, "Fault")], 0.3, 50), default);
        Assert.Equal("http://localhost:11434/v1/chat/completions", handler.Uri!.AbsoluteUri);
        Assert.Null(handler.Authorization);
        Assert.Equal("llama3.1:8b", handler.Body!["model"]!.GetValue<string>());
        Assert.Equal(50, handler.Body["max_tokens"]!.GetValue<int>());
        Assert.False(handler.Body.ContainsKey("max_completion_tokens"));
        Assert.Equal(0.3, handler.Body["temperature"]!.GetValue<double>());
        Assert.Equal(new[] { "system", "user" }, handler.Body["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal(new TokenUsage(3, 2, 5), response.Usage);
        Assert.Equal("Check isolation.", response.Content);
    }

    [Fact]
    public async Task ToolRequestsAndResultsRoundTripThroughCompatibleApi()
    {
        var handler = Fixtures.Handler(OpenAiCompletionTests.ToolsResponse());
        using var provider = new OllamaLlmProvider(Options, handler);
        var schema = JsonSerializer.SerializeToElement(new { type = "object" });
        var tools = new[] { new ToolDefinition("lookup", "Manual lookup", schema) };
        var result = await provider.CompleteWithToolsAsync(Fixtures.Query, tools, default);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Equal(7, result.ToolCalls[0].Arguments.GetProperty("page").GetInt32());
        Assert.Equal("object", handler.Body!["tools"]![0]!["function"]!["parameters"]!["type"]!.GetValue<string>());
        Assert.Equal(1, handler.Sends);
        await provider.CompleteWithToolsAsync(new CompletionRequest([new(LlmRole.Assistant, "", result.ToolCalls),
            new(LlmRole.Tool, "seven", toolCallId: "call-1", toolName: "lookup"),
            new(LlmRole.Tool, "eight", toolCallId: "call-2", toolName: "lookup")]), tools, default);
        Assert.Equal("lookup", handler.Body!["messages"]![0]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("call-2", handler.Body["messages"]![2]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("eight", handler.Body["messages"]![2]!["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65536)]
    public async Task CompatibleSsePreservesTextAndFinalUsage(int fragmentSize)
    {
        var stream = new FragmentedStream(Fixtures.Delta("مضخة") + Fixtures.Finish + Fixtures.Usage + Fixtures.Done, fragmentSize);
        using var provider = new OllamaLlmProvider(Options, new FakeHandler(_ => Task.FromResult(Fixtures.StreamResponse(stream))));
        var chunks = new List<StreamingChunk>();
        await foreach (var chunk in provider.StreamAsync(Fixtures.Query, default)) chunks.Add(chunk);
        Assert.Equal("مضخة", chunks[0].ContentDelta);
        Assert.True(chunks[^1].IsCompleted);
        Assert.Equal(new TokenUsage(3, 2, 5), chunks[^1].Usage);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task NativeEmbeddingsDisableTruncationAndPreserveBatchOrder()
    {
        var handler = Fixtures.Handler("""{"model":"embeddinggemma","embeddings":[[1,2],[3,4]]}""");
        using var provider = new OllamaLlmProvider(Options, handler);
        var result = await provider.GenerateEmbeddingsAsync(new(["first", "second"]), default);
        Assert.Equal("http://localhost:11434/api/embed", handler.Uri!.AbsoluteUri);
        Assert.Equal("embeddinggemma", handler.Body!["model"]!.GetValue<string>());
        Assert.False(handler.Body["truncate"]!.GetValue<bool>());
        Assert.Equal(new[] { "first", "second" }, handler.Body["input"]!.AsArray().Select(v => v!.GetValue<string>()));
        Assert.Equal(new float[] { 1, 2 }, result.Vectors[0]);
        Assert.Equal(new float[] { 3, 4 }, result.Vectors[1]);
    }

    [Theory]
    [InlineData("[[1]]")]
    [InlineData("[[],[]]")]
    [InlineData("[[1],[2,3]]")]
    [InlineData("[[1e100],[2]]")]
    [InlineData("[null,[2]]")]
    public async Task NativeEmbeddingValidationRejectsMalformedBatches(string vectors)
    {
        using var provider = new OllamaLlmProvider(Options, Fixtures.Handler("{\"model\":\"embed\",\"embeddings\":" + vectors + "}"));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.GenerateEmbeddingsAsync(new(["one", "two"]), default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task NativeEmbeddingReadsHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var stream = new FragmentedStream("{", blockAtEnd: true);
        using var provider = new OllamaLlmProvider(Options, new FakeHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(stream) })));
        var pending = provider.GenerateEmbeddingsAsync(new(["one"]), cancellation.Token);
        await stream.Waiting.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.True(stream.WasDisposed);
    }

    [Fact]
    public async Task MissingModelFailureIsSanitizedAndNotTransient()
    {
        using var provider = new OllamaLlmProvider(Options, new FakeHandler(_ => Task.FromResult(Fixtures.Json("private model path", HttpStatusCode.NotFound))));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.InvalidRequest, error.Kind);
        Assert.DoesNotContain("private", error.ToString());
    }
}
