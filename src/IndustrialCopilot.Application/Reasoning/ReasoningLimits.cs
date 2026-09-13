namespace IndustrialCopilot.Application.Reasoning;

public sealed class ReasoningLimits
{
    public int ModelTurns { get; }
    public int ToolCalls { get; }
    public int TopK { get; }
    public TimeSpan AgentTimeout { get; }
    public ReasoningLimits(int modelTurns = 4, int toolCalls = 4, int topK = 5, TimeSpan? agentTimeout = null)
    {
        var timeout = agentTimeout ?? TimeSpan.FromSeconds(60);
        if (modelTurns is < 1 or > 16 || toolCalls is < 1 or > 16 || topK is < 1 or > 20 || timeout <= TimeSpan.Zero || timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(modelTurns));
        ModelTurns = modelTurns; ToolCalls = toolCalls; TopK = topK; AgentTimeout = timeout;
    }
}
public sealed class InvalidAgentOutputException() : Exception("Agent output violated the trusted response contract.");
public sealed class AgentExecutionLimitException() : Exception("Agent execution budget exhausted.");
public sealed class AgentDependencyException() : Exception("An agent dependency failed.");
internal sealed class AgentTimedOutException : Exception;
