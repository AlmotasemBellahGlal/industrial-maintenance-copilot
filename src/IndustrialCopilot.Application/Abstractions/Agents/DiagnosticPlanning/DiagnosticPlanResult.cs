namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

// Success describes a produced proposal, not safety approval or permission to dispatch.
public sealed record DiagnosticPlanResult
{
    public AgentOutcome Outcome { get; }
    public DiagnosticPlan? Plan { get; }
    public string? Explanation { get; }

    private DiagnosticPlanResult(AgentOutcome outcome, DiagnosticPlan? payload, string? explanation)
    {
        Outcome = outcome;
        Plan = payload;
        Explanation = explanation;
    }

    public static DiagnosticPlanResult Success(DiagnosticPlan payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new(AgentOutcome.Success, payload, null);
    }

    public static DiagnosticPlanResult InsufficientEvidence(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.InsufficientEvidence, null, explanation);
    }

    public static DiagnosticPlanResult CannotProceed(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.CannotProceed, null, explanation);
    }
}
