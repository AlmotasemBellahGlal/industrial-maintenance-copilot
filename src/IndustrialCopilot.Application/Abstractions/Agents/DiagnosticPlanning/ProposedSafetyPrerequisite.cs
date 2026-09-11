using IndustrialCopilot.Application.Abstractions.Agents.Evidence;

namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

// Advisory only: an empty plan prerequisite list does not constitute a safety assessment.
public sealed record ProposedSafetyPrerequisite
{
    public string Description { get; }
    public IReadOnlyList<GroundedEvidence> Evidence { get; }

    public ProposedSafetyPrerequisite(string description, IReadOnlyList<GroundedEvidence> evidence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = evidence.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("Nonempty evidence with no null entries is required.", nameof(evidence));
        if (snapshot.Select(item => (item.DocumentId, item.ManualRevisionId, item.ChunkId)).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Duplicate citations are not allowed.", nameof(evidence));
        Description = description;
        Evidence = Array.AsReadOnly(snapshot);
    }
}
