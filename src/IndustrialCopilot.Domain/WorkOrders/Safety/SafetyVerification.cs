namespace IndustrialCopilot.Domain.WorkOrders.Safety;

public sealed record SafetyVerification
{
    public string VerifiedBy { get; }
    public DateTimeOffset VerifiedAt { get; }
    public string Evidence { get; }
    public bool IsSatisfied { get; }

    public SafetyVerification(string verifiedBy, DateTimeOffset verifiedAt, string evidence, bool isSatisfied)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verifiedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(evidence);
        if (verifiedAt == default) throw new ArgumentException("Verification time is required.", nameof(verifiedAt));
        VerifiedBy = verifiedBy;
        VerifiedAt = verifiedAt;
        Evidence = evidence;
        IsSatisfied = isSatisfied;
    }
}
