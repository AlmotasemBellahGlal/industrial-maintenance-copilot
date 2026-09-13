using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Abstractions.Safety;

/// <summary>Trusted deterministic assessment; never implemented by model inference.</summary>
public interface ISafetyPolicy
{
    // First validates a diagnostic plan; then validates the exact generated executable proposal.
    Task<SafetyAssessment> AssessAsync(DiagnosticPlan plan, WorkOrderProposal? proposal, CancellationToken cancellationToken);
}

public sealed record SafetyAssessment
{
    public bool CanProceed { get; }
    public IReadOnlyList<SafetyPrerequisite> Requirements { get; }
    private SafetyAssessment(bool allowed, SafetyPrerequisite[] requirements)
    { CanProceed = allowed; Requirements = Array.AsReadOnly(requirements); }
    public static SafetyAssessment Blocked() => new(false, []);
    /// <summary>Trusted policy alone establishes completeness, including an explicitly assessed empty set.</summary>
    public static SafetyAssessment Assessed(IReadOnlyList<SafetyPrerequisite> requirements)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        var copy = requirements.ToArray();
        if (copy.Any(p => p is null || p.Verification is not null) || copy.Select(p => p.Id).Distinct().Count() != copy.Length)
            throw new ArgumentException("Authoritative definitions must be unique, unverified and non-null.");
        return new(true, copy);
    }
}
