namespace IndustrialCopilot.Domain.WorkOrders;

public sealed record ApprovalDecision
{
    public string SupervisorId { get; }
    public int Revision { get; }
    public ApprovalDecisionKind Kind { get; }
    public DateTimeOffset DecidedAt { get; }

    internal ApprovalDecision(string supervisorId, int revision, ApprovalDecisionKind kind, DateTimeOffset decidedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(supervisorId);
        if (revision < 1) throw new ArgumentOutOfRangeException(nameof(revision));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (decidedAt == default) throw new ArgumentException("Decision time is required.", nameof(decidedAt));
        SupervisorId = supervisorId;
        Revision = revision;
        Kind = kind;
        DecidedAt = decidedAt;
    }
}
