using IndustrialCopilot.Application.Abstractions.Approval.Models;
using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Abstractions.Actions;

public enum TrustedAction { ReadEvidence=1, ReadEquipment=2, ValidateSafety=3, Approve=4, VerifySafety=5, Dispatch=6 }
/// <summary>Host checks authenticated context AND resource permissions. actorId alone is never a credential.</summary>
public interface IActionAuthorization
{
    Task<bool> AuthorizeAsync(string actorId, TrustedAction action, Guid resourceId, CancellationToken cancellationToken);
}
public enum DispatchAttemptState { Pending=1, Confirmed=2, DefinitivelyFailed=3, Uncertain=4 }
public enum ExternalDispatchOutcome { Accepted=1, DefinitivelyFailed=2, Uncertain=3 }
public enum DispatchGateOutcome { Ready=1, NotFound=2, Conflict=3, Forbidden=4, NotDispatchable=5 }
public enum DispatchGateFailure { RunLifecycle=1, HumanApproval=2, Safety=3 }
public sealed record DispatchAttempt(Guid Id, Guid WorkOrderId, int Revision, string RequestedToken, string ReservedToken,
    Guid? RunId, string? RunToken, string ActorId, DateTimeOffset ReservedAt, DispatchAttemptState State,
    bool InvocationStarted, string? ExternalReference, string? FailureCategory, WorkOrderContent Content);
public sealed record DispatchReservation(DispatchGateOutcome Outcome, DispatchAttempt? Attempt, DispatchGateFailure? Failure=null);
public sealed record DispatchCommand
{
    public WorkOrderReviewTarget Target { get; }
    public string ActorId { get; }
    public DispatchCommand(WorkOrderReviewTarget target,string actorId)
    { ArgumentNullException.ThrowIfNull(target); ArgumentException.ThrowIfNullOrWhiteSpace(actorId); Target=target; ActorId=actorId; }
}
public sealed record ExternalDispatchResult
{
    public ExternalDispatchOutcome Outcome { get; }
    public string? Reference { get; }
    private ExternalDispatchResult(ExternalDispatchOutcome outcome,string? reference) { Outcome=outcome; Reference=reference; }
    public static ExternalDispatchResult Accepted(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        if(reference.Length>200 || reference.Any(char.IsControl)) throw new ArgumentException("Invalid external reference.");
        return new(ExternalDispatchOutcome.Accepted,reference);
    }
    public static ExternalDispatchResult Failed() => new(ExternalDispatchOutcome.DefinitivelyFailed,null);
    public static ExternalDispatchResult Uncertain() => new(ExternalDispatchOutcome.Uncertain,null);
}
/// <summary>
/// Idempotency key is Attempt.Id. Same key MUST have the same content and at most one acceptance.
/// Reconcile is read-only and cannot dispatch. Missing/ambiguous receiver records mean Uncertain.
/// DefinitivelyFailed certifies non-acceptance and no outstanding delivery that can later accept;
/// adapters unable to guarantee that MUST return Uncertain. Never expire deduplication before business retention.
/// </summary>
public interface IExternalDispatch
{
    Task<ExternalDispatchResult> SendAsync(DispatchAttempt attempt,CancellationToken cancellationToken);
    Task<ExternalDispatchResult> ReconcileAsync(Guid idempotencyKey,CancellationToken cancellationToken);
}
public interface IDispatchAttemptStore
{
    Task<DispatchReservation> ReserveAsync(DispatchCommand command,CancellationToken cancellationToken);
    Task<DispatchAttempt?> GetAsync(Guid attemptId,CancellationToken cancellationToken);
    Task<DispatchAttemptSession?> OpenAsync(Guid attemptId,CancellationToken cancellationToken);
}
/// <summary>Exclusive durable-attempt coordination, including across processes. Dispose always releases ownership.</summary>
public abstract class DispatchAttemptSession : IAsyncDisposable
{
    public abstract DispatchAttempt Attempt { get; }
    public abstract Task MarkInvocationStartedAsync(CancellationToken cancellationToken);
    /// <summary>Restart only a definitively failed attempt whose exact reserved state is still current; key never changes.</summary>
    public abstract Task<bool> RestartAsync(string actorId,CancellationToken cancellationToken);
    /// <summary>Accepted atomically commits the Domain Dispatch transition and business audit.</summary>
    public abstract Task RecordAsync(ExternalDispatchResult result,string actorId,CancellationToken cancellationToken);
    public abstract ValueTask DisposeAsync();
}
public interface ISafetyVerificationService
{
    Task<VerificationResult> RecordAsync(WorkOrderReviewTarget target,Guid prerequisiteId,string actorId,string evidence,bool satisfied,CancellationToken cancellationToken);
}
public sealed record VerificationResult(DispatchGateOutcome Outcome, string? ConcurrencyToken);
public sealed record ManualReference(Guid ManualId,Guid ManualRevisionId);
public sealed record EquipmentContext(Guid EquipmentId,IReadOnlyList<ManualReference> Manuals);
public interface IEquipmentContextStore
{
    Task<EquipmentContext?> GetAsync(Guid equipmentId,CancellationToken cancellationToken);
}
