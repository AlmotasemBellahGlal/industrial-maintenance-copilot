namespace IndustrialCopilot.Domain.Manuals;

public sealed class ManualRevision
{
    public Guid Id { get; }
    public Guid ManualId { get; }
    public int Number { get; }

    internal ManualRevision(Guid id, Guid manualId, int number)
    {
        if (id == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(id));
        if (manualId == Guid.Empty) throw new ArgumentException("Manual identity is required.", nameof(manualId));
        if (number < 1) throw new ArgumentOutOfRangeException(nameof(number), "Revision number must be positive.");
        Id = id;
        ManualId = manualId;
        Number = number;
    }
}
