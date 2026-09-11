namespace IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

// Success describes a produced proposal, not safety approval or permission to dispatch.
public sealed record SymptomMatchResult
{
    public AgentOutcome Outcome { get; }
    public SymptomMatch? Match { get; }
    public string? Explanation { get; }

    private SymptomMatchResult(AgentOutcome outcome, SymptomMatch? payload, string? explanation)
    {
        Outcome = outcome;
        Match = payload;
        Explanation = explanation;
    }

    public static SymptomMatchResult Success(SymptomMatch payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new(AgentOutcome.Success, payload, null);
    }

    public static SymptomMatchResult InsufficientEvidence(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.InsufficientEvidence, null, explanation);
    }

    public static SymptomMatchResult CannotProceed(string explanation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        return new(AgentOutcome.CannotProceed, null, explanation);
    }
}
