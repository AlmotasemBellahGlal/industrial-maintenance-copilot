namespace IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

// Trusted host owns role/tool authorization and evidence resolution.
// Cancellation propagates as OperationCanceledException; technical failures remain exceptions.
public interface ISymptomMatcher
{
    Task<SymptomMatchResult> MatchAsync(SymptomMatchInput input, CancellationToken cancellationToken);
}
