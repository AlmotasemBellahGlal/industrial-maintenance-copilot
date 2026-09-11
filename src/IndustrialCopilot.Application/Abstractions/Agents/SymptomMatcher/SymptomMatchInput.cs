using IndustrialCopilot.Application.Abstractions.Agents.Evidence;

namespace IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

public sealed record SymptomMatchInput
{
    public string ReportedSymptom { get; }
    public IReadOnlyList<EquipmentManualCandidate> Candidates { get; }
    public IReadOnlyList<GroundedEvidence> InitialEvidence { get; }

    public SymptomMatchInput(string reportedSymptom, IReadOnlyList<EquipmentManualCandidate> candidates,
        IReadOnlyList<GroundedEvidence> initialEvidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(initialEvidence);
        var choices = candidates.ToArray();
        var evidence = initialEvidence.ToArray();
        if (choices.Length == 0 || choices.Any(item => item is null))
            throw new ArgumentException("Nonempty candidates with no null entries are required.", nameof(candidates));
        if (choices.Select(item => (item.EquipmentId, item.DocumentId, item.ManualRevisionId)).Distinct().Count() != choices.Length)
            throw new ArgumentException("Duplicate candidates are not allowed.", nameof(candidates));
        if (evidence.Any(item => item is null))
            throw new ArgumentException("Evidence cannot contain null.", nameof(initialEvidence));
        if (evidence.Select(item => (item.DocumentId, item.ManualRevisionId, item.ChunkId)).Distinct().Count() != evidence.Length)
            throw new ArgumentException("Duplicate citations are not allowed.", nameof(initialEvidence));
        if (evidence.Any(item => !choices.Any(candidate => candidate.DocumentId == item.DocumentId && candidate.ManualRevisionId == item.ManualRevisionId)))
            throw new ArgumentException("Evidence must reference a candidate document revision.", nameof(initialEvidence));
        ReportedSymptom = reportedSymptom;
        Candidates = Array.AsReadOnly(choices);
        InitialEvidence = Array.AsReadOnly(evidence);
    }
}
