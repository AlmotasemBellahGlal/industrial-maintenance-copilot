namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

/// <summary>Identifies reviewed content and the persisted state observed with it.</summary>
public sealed record WorkOrderReviewTarget
{
    public Guid WorkOrderId { get; }
    public int Revision { get; }
    /// <summary>Opaque equality token; changes for any relevant persisted mutation, not only scope edits.</summary>
    public string ConcurrencyToken { get; }

    public WorkOrderReviewTarget(Guid workOrderId, int revision, string concurrencyToken)
    {
        if (workOrderId == Guid.Empty) throw new ArgumentException("Work order identity is required.", nameof(workOrderId));
        if (revision <= 0) throw new ArgumentOutOfRangeException(nameof(revision));
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyToken);
        WorkOrderId = workOrderId;
        Revision = revision;
        ConcurrencyToken = concurrencyToken;
    }
}
