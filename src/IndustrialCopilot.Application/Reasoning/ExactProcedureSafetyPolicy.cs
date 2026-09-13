using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Reasoning;

/// <summary>Deployment-owned procedure, constructed from reviewed instructions, never from an agent response.</summary>
public sealed class ApprovedMaintenanceProcedure
{
    public EquipmentManualCandidate Candidate { get; }
    public IReadOnlyList<string> DiagnosticInstructions { get; }
    public IReadOnlyList<string> WorkInstructions { get; }
    public string WorkOrderDescription { get; }
    public SafetyAssessment Assessment { get; }
    public ApprovedMaintenanceProcedure(EquipmentManualCandidate candidate, IReadOnlyList<string> diagnosticInstructions,
        IReadOnlyList<string> workInstructions, string workOrderDescription, IReadOnlyList<SafetyPrerequisite> requirements, bool explicitlyNoMandatoryRequirements = false)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        Candidate = candidate;
        ArgumentException.ThrowIfNullOrWhiteSpace(workOrderDescription);
        WorkOrderDescription = workOrderDescription;
        DiagnosticInstructions = Copy(diagnosticInstructions);
        WorkInstructions = Copy(workInstructions);
        Assessment = SafetyAssessment.Assessed(requirements);
        if (!Assessment.Requirements.Any(p => p.IsMandatory) && !explicitlyNoMandatoryRequirements)
            throw new ArgumentException("Missing mandatory safety coverage must not imply an empty assessment.");
    }
    private static IReadOnlyList<string> Copy(IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var copy = values.ToArray();
        if (copy.Length == 0 || copy.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Reviewed instructions required.");
        return Array.AsReadOnly(copy);
    }
}

/// <summary>Fail-closed demo policy: exact scoped procedures, not generic industrial safety inference.</summary>
public sealed class ExactProcedureSafetyPolicy : ISafetyPolicy
{
    private readonly ApprovedMaintenanceProcedure[] procedures;
    public ExactProcedureSafetyPolicy(IEnumerable<ApprovedMaintenanceProcedure> procedures)
    {
        ArgumentNullException.ThrowIfNull(procedures);
        this.procedures = procedures.ToArray();
        if (this.procedures.Any(p => p is null) || this.procedures.GroupBy(p => (p.Candidate.EquipmentId,p.Candidate.DocumentId,p.Candidate.ManualRevisionId)).Any(g => g.Count() != 1))
            throw new ArgumentException("One trusted procedure per equipment/manual revision is required.");
    }
    public Task<SafetyAssessment> AssessAsync(DiagnosticPlan plan, WorkOrderProposal? proposal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan); cancellationToken.ThrowIfCancellationRequested();
        var rule = procedures.SingleOrDefault(p => p.Candidate == plan.SelectedCandidate);
        var valid = rule is not null && plan.Steps.Select(s => s.Instruction).SequenceEqual(rule.DiagnosticInstructions)
            && plan.SafetyPrerequisites.All(p => rule.Assessment.Requirements.Any(r => r.Description == p.Description));
        if (proposal is not null)
            valid = valid && proposal.SelectedCandidate == plan.SelectedCandidate
                && proposal.Description == rule!.WorkOrderDescription
                && proposal.Actions.Select(a => a.Instruction).SequenceEqual(rule!.WorkInstructions)
                && proposal.SafetyPrerequisites.All(p => rule.Assessment.Requirements.Any(r => r.Description == p.Description));
        return Task.FromResult(valid ? rule!.Assessment : SafetyAssessment.Blocked());
    }
}
