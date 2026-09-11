using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

public sealed record DiagnosticPlan
{
    public EquipmentManualCandidate SelectedCandidate { get; }
    public IReadOnlyList<ProposedDiagnosticStep> Steps { get; }
    public IReadOnlyList<ProposedSafetyPrerequisite> SafetyPrerequisites { get; }

    public DiagnosticPlan(EquipmentManualCandidate selectedCandidate, IReadOnlyList<ProposedDiagnosticStep> steps,
        IReadOnlyList<ProposedSafetyPrerequisite> safetyPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidate);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(safetyPrerequisites);
        var ordered = steps.ToArray();
        var prerequisites = safetyPrerequisites.ToArray();
        if (ordered.Length == 0 || ordered.Any(item => item is null))
            throw new ArgumentException("Nonempty ordered instructions are required.", nameof(steps));
        for (var index = 1; index < ordered.Length; index++)
            if (ordered[index].Order <= ordered[index - 1].Order)
                throw new ArgumentException("Orders must be strictly increasing.", nameof(steps));
        if (prerequisites.Any(item => item is null))
            throw new ArgumentException("Prerequisites cannot contain null.", nameof(safetyPrerequisites));
        var evidence = ordered.SelectMany(item => item.Evidence).Concat(prerequisites.SelectMany(item => item.Evidence));
        if (evidence.Any(item => item.DocumentId != selectedCandidate.DocumentId || item.ManualRevisionId != selectedCandidate.ManualRevisionId))
            throw new ArgumentException("Evidence must match the selected document revision.");
        SelectedCandidate = selectedCandidate;
        Steps = Array.AsReadOnly(ordered);
        SafetyPrerequisites = Array.AsReadOnly(prerequisites);
    }
}
