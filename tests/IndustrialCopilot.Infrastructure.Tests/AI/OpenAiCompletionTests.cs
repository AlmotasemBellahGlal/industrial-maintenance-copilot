using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.AI;

namespace IndustrialCopilot.Infrastructure.Tests.AI;

public class OpenAiCompletionTests
{
    [Fact]
    public async Task MapsOrderedConversationOptionsAndRequiredUsage()
    {
        var handler = Fixtures.Handler(Fixtures.Completion);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var result = await provider.CompleteAsync(new CompletionRequest([
            new(LlmRole.System, "Ground answers"), new(LlmRole.User, "Fault"), new(LlmRole.Assistant, "Checking")], 0.4, 128), default);
        Assert.Equal("https://api.openai.com/v1/chat/completions", handler.Uri!.AbsoluteUri);
        Assert.Equal("Bearer test-key-not-a-credential", handler.Authorization);
        Assert.Equal("test-chat-model", handler.Body!["model"]!.GetValue<string>());
        Assert.Equal(0.4, handler.Body["temperature"]!.GetValue<double>());
        Assert.Equal(128, handler.Body["max_completion_tokens"]!.GetValue<int>());
        Assert.Equal(new[] { "system", "user", "assistant" }, handler.Body["messages"]!.AsArray().Select(m => m!["role"]!.GetValue<string>()));
        Assert.Equal(new[] { "Ground answers", "Fault", "Checking" }, handler.Body["messages"]!.AsArray().Select(m => m!["content"]!.GetValue<string>()));
        Assert.Equal("Check isolation.", result.Content);
        Assert.Equal(new TokenUsage(3, 2, 5), result.Usage);
        Assert.Equal("actual-model", result.Model);
    }

    [Fact]
    public async Task DefaultsOutputBudgetAndDoesNotClampFiniteTemperature()
    {
        var handler = Fixtures.Handler(Fixtures.Completion);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        await provider.CompleteAsync(Fixtures.Query, default);
        Assert.False(handler.Body!.ContainsKey("temperature"));
        Assert.Equal(4096,handler.Body["max_completion_tokens"]!.GetValue<int>());
        await provider.CompleteAsync(new CompletionRequest(Fixtures.Query.Messages, 3), default);
        Assert.Equal(3, handler.Body!["temperature"]!.GetValue<double>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"prompt_tokens\":-1,\"completion_tokens\":2,\"total_tokens\":1}")]
    [InlineData("{\"prompt_tokens\":3,\"completion_tokens\":2,\"total_tokens\":9}")]
    [InlineData("{\"prompt_tokens\":2147483647,\"completion_tokens\":2,\"total_tokens\":1}")]
    public async Task RejectsMissingOrMalformedRequiredUsage(string? usage)
    {
        var json = JsonNode.Parse(Fixtures.Completion)!;
        if (usage is null) json.AsObject().Remove("usage"); else json["usage"] = JsonNode.Parse(usage);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json.ToJsonString()));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
    }

    internal static string ToolsResponse(string arguments = "{\"page\":7}")
    {
        var json = JsonNode.Parse(Fixtures.Completion)!;
        var choice = json["choices"]![0]!;
        choice["finish_reason"] = "tool_calls";
        choice["message"]!["content"] = null;
        choice["message"]!["tool_calls"] = new JsonArray(
            new JsonObject { ["id"] = "call-1", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "lookup", ["arguments"] = arguments } },
            new JsonObject { ["id"] = "call-2", ["type"] = "function", ["function"] = new JsonObject { ["name"] = "lookup", ["arguments"] = "{\"page\":8}" } });
        return json.ToJsonString();
    }

    [Fact]
    public async Task MapsSchemasMultipleToolRequestsAndCorrelatedHistoryWithoutExecutingTools()
    {
        var handler = Fixtures.Handler(ToolsResponse());
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        using var schema = JsonDocument.Parse("""{"type":"object","properties":{"page":{"type":"integer"}}} """);
        var tools = new[] { new ToolDefinition("lookup", "Read manual", schema.RootElement) };
        var result = await provider.CompleteWithToolsAsync(Fixtures.Query, tools, default);
        Assert.Empty(result.Content);
        Assert.Equal(2, result.ToolCalls.Count);
        Assert.Equal(7, result.ToolCalls[0].Arguments.GetProperty("page").GetInt32());
        Assert.Equal(8, result.ToolCalls[1].Arguments.GetProperty("page").GetInt32());
        Assert.Equal(new TokenUsage(3, 2, 5), result.Usage);
        Assert.Equal("integer", handler.Body!["tools"]![0]!["function"]!["parameters"]!["properties"]!["page"]!["type"]!.GetValue<string>());
        Assert.Equal("Read manual", handler.Body["tools"]![0]!["function"]!["description"]!.GetValue<string>());
        Assert.Equal(1, handler.Sends);

        var history = new CompletionRequest([new(LlmRole.User, "Fault"), new(LlmRole.Assistant, "", result.ToolCalls),
            new(LlmRole.Tool, "Page seven", toolCallId: "call-1", toolName: "lookup"),
            new(LlmRole.Tool, "Page eight", toolCallId: "call-2", toolName: "lookup")]);
        await provider.CompleteWithToolsAsync(history, tools, default);
        var messages = handler.Body!["messages"]!;
        Assert.Equal("call-1", messages[1]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("lookup", messages[1]!["tool_calls"]![0]!["function"]!["name"]!.GetValue<string>());
        Assert.Equal("{\"page\":7}", messages[1]!["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
        Assert.Equal("call-2", messages[3]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("tool", messages[3]!["role"]!.GetValue<string>());
        Assert.Equal("Page eight", messages[3]!["content"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("invalid-json")]
    [InlineData("[]")]
    [InlineData("null")]
    public async Task RejectsMalformedToolArguments(string arguments)
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(ToolsResponse(arguments)));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteWithToolsAsync(Fixtures.Query, [], default));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse, error.Kind);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public async Task RejectsDuplicateToolCallIdsAndContradictoryFinishReason()
    {
        var json = JsonNode.Parse(ToolsResponse())!;
        json["choices"]![0]!["message"]!["tool_calls"]![1]!["id"] = "call-1";
        using var duplicate = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json.ToJsonString()));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse,
            (await Assert.ThrowsAsync<LlmProviderException>(() => duplicate.CompleteWithToolsAsync(Fixtures.Query, [], default))).Kind);
        json = JsonNode.Parse(ToolsResponse())!;
        json["choices"]![0]!["finish_reason"] = "stop";
        using var contradictory = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json.ToJsonString()));
        Assert.Equal(LlmProviderFailureKind.InvalidResponse,
            (await Assert.ThrowsAsync<LlmProviderException>(() => contradictory.CompleteWithToolsAsync(Fixtures.Query, [], default))).Kind);
    }

    [Fact]
    public async Task RejectsLegacyCallEvenIfProviderIncorrectlySaysStop()
    {
        var json = JsonNode.Parse(Fixtures.Completion)!;
        json["choices"]![0]!["message"]!["function_call"] = new JsonObject { ["name"] = "lookup", ["arguments"] = "{}" };
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json.ToJsonString()));
        Assert.Equal(LlmProviderFailureKind.UnsupportedResponse,
            (await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default))).Kind);
    }

    [Theory]
    [InlineData("missing", "lookup")]
    [InlineData("call-1", "other-name")]
    public async Task RejectsMismatchedToolResultCorrelationBeforeSending(string id, string name)
    {
        var handler = Fixtures.Handler(Fixtures.Completion);
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), handler);
        var call = new ToolCall("call-1", "lookup", JsonSerializer.SerializeToElement(new { page = 1 }));
        var request = new CompletionRequest([new(LlmRole.Assistant, "", [call]), new(LlmRole.Tool, "result", toolCallId: id, toolName: name)]);
        await Assert.ThrowsAsync<ArgumentException>(() => provider.CompleteAsync(request, default));
        Assert.Equal(0, handler.Sends);
    }

    [Fact]
    public async Task TextOnlyMethodCannotSilentlyDropToolCalls()
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(ToolsResponse()));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(LlmProviderFailureKind.UnsupportedResponse, error.Kind);
    }

    [Fact]
    public async Task ToolMethodAlsoSupportsNormalAssistantResponse()
    {
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(Fixtures.Completion));
        var result = await provider.CompleteWithToolsAsync(Fixtures.Query, [], default);
        Assert.Empty(result.ToolCalls);
        Assert.Equal("Check isolation.", result.Content);
    }

    [Theory]
    [InlineData("length", LlmProviderFailureKind.UnsupportedResponse)]
    [InlineData("content_filter", LlmProviderFailureKind.Refused)]
    [InlineData("function_call", LlmProviderFailureKind.UnsupportedResponse)]
    public async Task DoesNotPresentTruncatedOrUnsupportedResponsesAsSuccess(string reason, LlmProviderFailureKind kind)
    {
        var json = JsonNode.Parse(Fixtures.Completion)!;
        json["choices"]![0]!["finish_reason"] = reason;
        using var provider = new OpenAiLlmProvider(Fixtures.Options(), Fixtures.Handler(json.ToJsonString()));
        var error = await Assert.ThrowsAsync<LlmProviderException>(() => provider.CompleteAsync(Fixtures.Query, default));
        Assert.Equal(kind, error.Kind);
    }
}
