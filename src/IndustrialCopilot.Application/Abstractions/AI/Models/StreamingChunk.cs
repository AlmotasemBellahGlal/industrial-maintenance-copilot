namespace IndustrialCopilot.Application.Abstractions.AI.Models;

// Usage is the final total on the completed chunk, not a per-chunk delta.
public sealed record StreamingChunk
{
    public string ContentDelta { get; }
    public bool IsCompleted { get; }
    public TokenUsage? Usage { get; }

    public StreamingChunk(string contentDelta, bool isCompleted, TokenUsage? usage = null)
    {
        ArgumentNullException.ThrowIfNull(contentDelta);
        if (!isCompleted && usage is not null) throw new ArgumentException("Final usage requires a completed chunk.", nameof(usage));
        ContentDelta = contentDelta;
        IsCompleted = isCompleted;
        Usage = usage;
    }
}
