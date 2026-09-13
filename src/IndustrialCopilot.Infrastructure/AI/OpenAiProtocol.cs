using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

internal static class OpenAiProtocol
{
    internal static JsonObject ChatRequest(OpenAiOptions options, CompletionRequest request,
        bool stream, IReadOnlyList<ToolDefinition>? tools = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Messages.Count == 0) throw new ArgumentException("At least one message is required.", nameof(request));
        var messages = new JsonArray();
        var calls = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var message in request.Messages)
        {
            var item = new JsonObject { ["role"] = message.Role.ToString().ToLowerInvariant(), ["content"] = message.Content };
            if (message.ToolCalls.Count > 0)
            {
                var history = new JsonArray();
                foreach (var call in message.ToolCalls)
                {
                    if (!calls.TryAdd(call.Id, call.Name)) throw new ArgumentException("Duplicate tool call identity.", nameof(request));
                    history.Add(new JsonObject { ["id"] = call.Id, ["type"] = "function",
                        ["function"] = new JsonObject { ["name"] = call.Name, ["arguments"] = call.Arguments.GetRawText() } });
                }
                item["tool_calls"] = history;
            }
            if (message.Role == LlmRole.Tool)
            {
                // OpenAI correlates results by ID. The name is preserved on the assistant call,
                // and checked here rather than sent as an undocumented tool-result field.
                if (!calls.Remove(message.ToolCallId!, out var name) || name != message.ToolName)
                    throw new ArgumentException("Tool result must match a preceding tool call and name.", nameof(request));
                item["tool_call_id"] = message.ToolCallId;
            }
            messages.Add(item);
        }
        var payload = new JsonObject { ["model"] = options.ChatModel, ["messages"] = messages, ["stream"] = stream, ["n"] = 1 };
        if (request.Temperature is { } temperature) payload["temperature"] = temperature;
        if (request.MaxTokens is { } maxTokens) payload["max_completion_tokens"] = maxTokens;
        if (stream) payload["stream_options"] = new JsonObject { ["include_usage"] = true };
        if (tools is not null && tools.Count > 0)
        {
            var definitions = new JsonArray();
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var tool in tools)
            {
                if (tool is null || !names.Add(tool.Name)) throw new ArgumentException("Tools must be non-null and uniquely named.", nameof(tools));
                definitions.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject
                { ["name"] = tool.Name, ["description"] = tool.Description, ["parameters"] = JsonNode.Parse(tool.ArgumentsSchema.GetRawText()) } });
            }
            payload["tools"] = definitions;
        }
        return payload;
    }

    internal static TokenUsage Usage(JsonElement element)
    {
        var prompt = element.GetProperty("prompt_tokens").GetInt32();
        var completion = element.GetProperty("completion_tokens").GetInt32();
        var total = element.GetProperty("total_tokens").GetInt32();
        if (prompt < 0 || completion < 0 || total < 0 || checked(prompt + completion) != total) throw Invalid();
        return new(prompt, completion, total);
    }

    internal static ToolCompletionResponse Completion(JsonElement root)
    {
        var choice = SingleChoice(root);
        var reason = RequiredText(choice, "finish_reason");
        CheckFinish(reason);
        var message = choice.GetProperty("message");
        if (RequiredText(message, "role") != "assistant") throw Invalid();
        if (message.TryGetProperty("function_call", out var legacyCall) && legacyCall.ValueKind != JsonValueKind.Null)
            throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
        if (message.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
            throw new LlmProviderException(LlmProviderFailureKind.Refused);
        var calls = new List<ToolCall>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (message.TryGetProperty("tool_calls", out var toolCalls) && toolCalls.ValueKind != JsonValueKind.Null)
            foreach (var call in toolCalls.EnumerateArray())
            {
                if (RequiredText(call, "type") != "function") throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
                var id = RequiredText(call, "id");
                if (!ids.Add(id)) throw Invalid();
                var function = call.GetProperty("function");
                using var arguments = JsonDocument.Parse(RequiredText(function, "arguments"));
                calls.Add(new ToolCall(id, RequiredText(function, "name"), arguments.RootElement));
            }
        if ((reason == "tool_calls") != (calls.Count > 0)) throw Invalid();
        var content = message.GetProperty("content");
        var text = content.ValueKind == JsonValueKind.Null && calls.Count > 0 ? "" : content.GetString();
        if (text is null) throw Invalid();
        return new(text, calls, Usage(root.GetProperty("usage")), RequiredText(root, "model"));
    }

    internal static EmbeddingResult Embeddings(JsonElement root, int count)
    {
        var data = root.GetProperty("data");
        if (data.GetArrayLength() != count) throw Invalid();
        var vectors = new IReadOnlyList<float>[count];
        var dimension = 0;
        foreach (var item in data.EnumerateArray())
        {
            var index = item.GetProperty("index").GetInt32();
            if (index < 0 || index >= count || vectors[index] is not null) throw Invalid();
            var vector = item.GetProperty("embedding").EnumerateArray().Select(value => value.GetSingle()).ToArray();
            if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value)) || (dimension != 0 && vector.Length != dimension)) throw Invalid();
            dimension = vector.Length;
            vectors[index] = vector;
        }
        if (vectors.Any(vector => vector is null)) throw Invalid();
        return new(vectors, RequiredText(root, "model"));
    }

    internal static JsonElement SingleChoice(JsonElement root)
    {
        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() != 1 || choices[0].GetProperty("index").GetInt32() != 0) throw Invalid();
        return choices[0];
    }

    internal static void CheckFinish(string reason)
    {
        if (reason == "content_filter") throw new LlmProviderException(LlmProviderFailureKind.Refused);
        if (reason is not ("stop" or "tool_calls")) throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
    }

    internal static string RequiredText(JsonElement element, string name)
    {
        var text = element.GetProperty(name).GetString();
        if (string.IsNullOrWhiteSpace(text)) throw Invalid();
        return text;
    }

    internal static LlmProviderException Invalid() => new(LlmProviderFailureKind.InvalidResponse);
}
