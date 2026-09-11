namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

public sealed record SubmitWorkOrderReviewRequest
{
    public WorkOrderReviewTarget Target { get; }
    /// <summary>Supplied by trusted authenticated host context, never by an agent or untrusted request body.</summary>
    public string ActorId { get; }

    public SubmitWorkOrderReviewRequest(WorkOrderReviewTarget target, string actorId)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        Target = target;
        ActorId = actorId;
    }
}
