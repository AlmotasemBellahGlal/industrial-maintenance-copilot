namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

// Trusted host owns role/tool authorization and evidence resolution.
// Cancellation propagates as OperationCanceledException; technical failures remain exceptions.
public interface IDiagnosticSafetyPlanner
{
    Task<DiagnosticPlanResult> PlanAsync(DiagnosticPlanInput input, CancellationToken cancellationToken);
}
