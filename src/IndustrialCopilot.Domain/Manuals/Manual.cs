namespace IndustrialCopilot.Domain.Manuals;

public sealed class Manual
{
    private readonly List<ManualRevision> revisions = [];

    public Guid Id { get; }
    public Guid EquipmentId { get; }
    public string Title { get; }
    public IReadOnlyList<ManualRevision> Revisions => revisions.AsReadOnly();

    public Manual(Guid id, Guid equipmentId, string title)
    {
        if (id == Guid.Empty) throw new ArgumentException("Manual identity is required.", nameof(id));
        if (equipmentId == Guid.Empty) throw new ArgumentException("Equipment identity is required.", nameof(equipmentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Id = id;
        EquipmentId = equipmentId;
        Title = title;
    }

    public ManualRevision PublishRevision(Guid revisionId, int number)
    {
        var revision = new ManualRevision(revisionId, Id, number);
        if (revisions.Any(r => r.Id == revisionId || r.Number == number))
            throw new InvalidOperationException("Revision identities and numbers must be unique within a manual.");
        revisions.Add(revision);
        return revision;
    }
}
