namespace IndustrialCopilot.Domain.WorkOrders.Safety;

public sealed class SafetyPrerequisite
{
    public Guid Id { get; }
    public string Description { get; }
    public bool IsMandatory { get; }
    public SafetyVerification? Verification { get; }
    public SafetyPrerequisiteStatus Status => Verification is null
        ? SafetyPrerequisiteStatus.Unverified
        : Verification.IsSatisfied ? SafetyPrerequisiteStatus.Satisfied : SafetyPrerequisiteStatus.Unsatisfied;

    public SafetyPrerequisite(Guid id, string description, bool isMandatory)
        : this(id, description, isMandatory, null) { }

    /// <summary>Restores an existing verification, not a new safety assessment.</summary>
    public static SafetyPrerequisite Restore(Guid id, string description, bool isMandatory, SafetyVerification? verification) =>
        new(id, description, isMandatory, verification);

    private SafetyPrerequisite(Guid id, string description, bool isMandatory, SafetyVerification? verification)
    {
        if (id == Guid.Empty) throw new ArgumentException("Prerequisite identity is required.", nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Id = id;
        Description = description;
        IsMandatory = isMandatory;
        Verification = verification;
    }

    internal SafetyPrerequisite WithVerification(SafetyVerification? verification) =>
        new(Id, Description, IsMandatory, verification);
}
