namespace IndustrialCopilot.Domain.Equipment;

public sealed class Equipment
{
    public Guid Id { get; }
    public string Name { get; }

    public Equipment(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException("Equipment identity is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
    }
}
