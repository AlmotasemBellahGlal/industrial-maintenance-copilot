using IndustrialCopilot.Application.Abstractions.AI;
namespace IndustrialCopilot.Application.Reasoning;

public sealed class ReasoningLimits
{
    public int ModelTurns { get; }
    public int ToolCalls { get; }
    public int TopK { get; }
    public TimeSpan AgentTimeout { get; }
    public int Attempts { get; }
    public TimeSpan RetryDelay { get; }
    public TimeSpan FallbackTimeout { get; }
    public ReasoningLimits(int modelTurns = 4, int toolCalls = 4, int topK = 5, TimeSpan? agentTimeout = null, int attempts = 2, TimeSpan? retryDelay = null, TimeSpan? fallbackTimeout = null)
    {
        var timeout = agentTimeout ?? TimeSpan.FromSeconds(60);
        if (modelTurns is < 1 or > 16 || toolCalls is < 1 or > 16 || topK is < 1 or > 20 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(modelTurns));
        var delay=retryDelay??TimeSpan.FromMilliseconds(250); var fallback=fallbackTimeout??TimeSpan.FromSeconds(15);
        if(attempts is <1 or >3 || delay<TimeSpan.Zero || delay>TimeSpan.FromSeconds(5) || fallback<=TimeSpan.Zero || fallback>TimeSpan.FromSeconds(60))throw new ArgumentOutOfRangeException(nameof(attempts));
        Attempts=attempts;RetryDelay=delay;FallbackTimeout=fallback;
        ModelTurns = modelTurns; ToolCalls = toolCalls; TopK = topK; AgentTimeout = timeout;
    }
}
public sealed class InvalidAgentOutputException() : Exception("Agent output violated the trusted response contract.");
public sealed class AgentExecutionLimitException() : Exception("Agent execution budget exhausted.");
public sealed class AgentDependencyException(DependencyFailureKind failure = DependencyFailureKind.Terminal) : DependencyFailureException(failure);
internal sealed class AgentTimedOutException : Exception;
