namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record TokenUsage
{
    public int PromptTokens { get; }
    public int CompletionTokens { get; }
    public int TotalTokens { get; }

    public TokenUsage(int promptTokens, int completionTokens, int totalTokens)
    {
        if (promptTokens < 0) throw new ArgumentOutOfRangeException(nameof(promptTokens));
        if (completionTokens < 0) throw new ArgumentOutOfRangeException(nameof(completionTokens));
        if (totalTokens < 0) throw new ArgumentOutOfRangeException(nameof(totalTokens));
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        TotalTokens = totalTokens;
    }
}
