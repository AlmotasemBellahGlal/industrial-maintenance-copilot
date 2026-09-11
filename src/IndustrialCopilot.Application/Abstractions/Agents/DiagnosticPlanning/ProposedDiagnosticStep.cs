using IndustrialCopilot.Application.Abstractions.Agents.Evidence;

namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

public sealed record ProposedDiagnosticStep
{
    public int Order { get; }
    public string Instruction { get; }
    public IReadOnlyList<GroundedEvidence> Evidence { get; }

    public ProposedDiagnosticStep(int order, string instruction, IReadOnlyList<GroundedEvidence> evidence)
    {
        if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        ArgumentNullException.ThrowIfNull(evidence);
        var snapshot = evidence.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("Nonempty evidence with no null entries is required.", nameof(evidence));
        if (snapshot.Select(item => (item.DocumentId, item.ManualRevisionId, item.ChunkId)).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Duplicate citations are not allowed.", nameof(evidence));
        Order = order;
        Instruction = instruction;
        Evidence = Array.AsReadOnly(snapshot);
    }
}
