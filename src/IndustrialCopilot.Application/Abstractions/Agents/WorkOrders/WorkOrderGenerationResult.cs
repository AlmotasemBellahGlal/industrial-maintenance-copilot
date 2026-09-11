namespace IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

// Success describes a produced proposal, not safety approval or permission to dispatch.
public sealed record WorkOrderGenerationResult
{
    public AgentOutcome Outcome { get; }
    public WorkOrderProposal? Proposal { get; }
    public string? Explanation { get; }

    private WorkOrderGenerationResult(AgentOutcome outcome, WorkOrderProposal? payload, string? explanation)
    {
        Outcome = outcome;
        Proposal = payload;
        Explanation = explanation;
    }

    public static WorkOrderGenerationResult Success(WorkOrderProposal payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new(AgentOutcome.Success, payload, null);
    }

    public static WorkOrderGenerationResult InsufficientEvidence(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.InsufficientEvidence, null, explanation);
    }

    public static WorkOrderGenerationResult CannotProceed(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.CannotProceed, null, explanation);
    }
}
