namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record LlmMessage
{
    public LlmRole Role { get; }
    public string Content { get; }
    public IReadOnlyList<ToolCall> ToolCalls { get; }
    public string? ToolCallId { get; }
    public string? ToolName { get; }

    public LlmMessage(LlmRole role, string content, IReadOnlyList<ToolCall>? toolCalls = null,
        string? toolCallId = null, string? toolName = null)
    {
        if (!Enum.IsDefined(role)) throw new ArgumentOutOfRangeException(nameof(role));
        ArgumentNullException.ThrowIfNull(content);
        var calls = toolCalls?.ToArray() ?? [];
        if (calls.Any(call => call is null)) throw new ArgumentException("Tool calls cannot contain null.", nameof(toolCalls));
        if (role != LlmRole.Assistant && calls.Length > 0)
            throw new ArgumentException("Only assistant messages can request tools.", nameof(toolCalls));
        if (role == LlmRole.Tool)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(toolCallId);
            ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        }
        else if (toolCallId is not null || toolName is not null)
            throw new ArgumentException("Only tool results can carry result correlation metadata.");
        Role = role;
        Content = content;
        ToolCalls = Array.AsReadOnly(calls);
        ToolCallId = toolCallId;
        ToolName = toolName;
    }
}
