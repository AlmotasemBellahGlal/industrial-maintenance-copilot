using IndustrialCopilot.Domain.Evidence;

namespace IndustrialCopilot.Domain.Diagnostics;

public sealed record DiagnosticStep
{
    public int Order { get; }
    public string Instruction { get; }
    public IReadOnlyList<EvidenceReference> Evidence { get; }

    public DiagnosticStep(int order, string instruction, IEnumerable<EvidenceReference> evidence)
    {
        if (order < 1) throw new ArgumentOutOfRangeException(nameof(order), "Step order must be positive.");
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        ArgumentNullException.ThrowIfNull(evidence);
        var references = evidence.ToArray();
        if (references.Length == 0 || references.Any(reference => reference is null))
            throw new ArgumentException("At least one evidence reference is required, with no null entries.", nameof(evidence));
        Order = order;
        Instruction = instruction;
        Evidence = Array.AsReadOnly(references);
    }

    public bool Equals(DiagnosticStep? other) =>
        other is not null && Order == other.Order && Instruction == other.Instruction
        && Evidence.SequenceEqual(other.Evidence);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Order);
        hash.Add(Instruction);
        foreach (var reference in Evidence) hash.Add(reference);
        return hash.ToHashCode();
    }
}
