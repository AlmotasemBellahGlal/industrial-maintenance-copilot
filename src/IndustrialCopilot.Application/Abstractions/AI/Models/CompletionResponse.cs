namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record CompletionResponse
{
    public string Content { get; }
    public TokenUsage Usage { get; }
    public string? Model { get; }

    public CompletionResponse(string content, TokenUsage usage, string? model = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(usage);
        Content = content;
        Usage = usage;
        Model = model;
    }
}
