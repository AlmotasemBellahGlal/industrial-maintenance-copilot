using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

namespace IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

// Proposal only; conveys no safety, approval, or dispatch authority.
public sealed record WorkOrderProposal
{
    public EquipmentManualCandidate SelectedCandidate { get; }
    public string ReportedSymptom { get; }
    public string Description { get; }
    public IReadOnlyList<ProposedWorkOrderAction> Actions { get; }
    public IReadOnlyList<ProposedSafetyPrerequisite> SafetyPrerequisites { get; }

    public WorkOrderProposal(EquipmentManualCandidate selectedCandidate, string reportedSymptom, string description,
        IReadOnlyList<ProposedWorkOrderAction> actions,
        IReadOnlyList<ProposedSafetyPrerequisite> safetyPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidate);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(safetyPrerequisites);
        var ordered = actions.ToArray();
        var prerequisites = safetyPrerequisites.ToArray();
        if (ordered.Length == 0 || ordered.Any(item => item is null))
            throw new ArgumentException("Nonempty ordered instructions are required.", nameof(actions));
        for (var index = 1; index < ordered.Length; index++)
            if (ordered[index].Order <= ordered[index - 1].Order)
                throw new ArgumentException("Orders must be strictly increasing.", nameof(actions));
        if (prerequisites.Any(item => item is null))
            throw new ArgumentException("Prerequisites cannot contain null.", nameof(safetyPrerequisites));
        var evidence = ordered.SelectMany(item => item.Evidence).Concat(prerequisites.SelectMany(item => item.Evidence));
        if (evidence.Any(item => item.DocumentId != selectedCandidate.DocumentId || item.ManualRevisionId != selectedCandidate.ManualRevisionId))
            throw new ArgumentException("Evidence must match the selected document revision.");
        SelectedCandidate = selectedCandidate;
        ReportedSymptom = reportedSymptom;
        Description = description;
        Actions = Array.AsReadOnly(ordered);
        SafetyPrerequisites = Array.AsReadOnly(prerequisites);
    }
}
