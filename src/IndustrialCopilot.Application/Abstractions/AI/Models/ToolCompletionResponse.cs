namespace IndustrialCopilot.Application.Abstractions.AI.Models;

// Content may be empty when the model returns only tool calls.
public sealed record ToolCompletionResponse
{
    public string Content { get; }
    public IReadOnlyList<ToolCall> ToolCalls { get; }
    public TokenUsage? Usage { get; }
    public string? Model { get; }

    public ToolCompletionResponse(string content, IReadOnlyList<ToolCall> toolCalls, TokenUsage? usage, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(toolCalls);
        var snapshot = toolCalls.ToArray();
        if (snapshot.Any(call => call is null)) throw new ArgumentException("Tool calls cannot contain null.", nameof(toolCalls));
        Content = content;
        ToolCalls = Array.AsReadOnly(snapshot);
        Usage = usage;
        Model = model;
    }
}
