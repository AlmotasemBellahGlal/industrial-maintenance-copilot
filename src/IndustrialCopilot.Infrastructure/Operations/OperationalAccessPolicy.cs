using IndustrialCopilot.Application.Abstractions.Approval.Models;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>
/// Required trusted-host boundary. No permissive default is supplied. Implementations use authenticated
/// context, authorize resource access and verify the exact edited scope/consent through trusted validation.
/// DTO construction and actor strings are never evidence of authorization.
/// </summary>
public abstract class OperationalAccessPolicy
{
    public abstract Task<bool> CanReadWorkOrderAsync(Guid id,string actorId,CancellationToken token);
    public abstract Task<bool> CanSubmitAsync(SubmitWorkOrderReviewRequest request,CancellationToken token);
    /// <returns>Null when authorized and validated; otherwise Forbidden, InvalidEdit or SafetyValidationFailed.</returns>
    public abstract Task<ApprovalOperationOutcome?> ValidateDecisionAsync(RecordWorkOrderDecisionRequest request,CancellationToken token);
    public abstract Task<bool> CanAccessTraceAsync(Guid executionId,bool write,CancellationToken token);
}
