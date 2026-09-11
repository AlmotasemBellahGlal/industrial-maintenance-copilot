using IndustrialCopilot.Application.Abstractions.Approval.Models;

namespace IndustrialCopilot.Application.Abstractions.Approval;

/// <summary>Trusted human-review use cases. Never an agent tool or dispatch capability.</summary>
/// <remarks>
/// The host supplies authenticated actor identities; implementations enforce access and supervisor
/// authorization on every decision. Constructing a request or snapshot grants no authority.
/// Compare both target revision and opaque concurrency token atomically with persistence of the
/// Domain transition and audit record. Tokens must change for verification, decisions and lifecycle
/// changes as well as scope edits. Stale targets return Conflict; never silently rebase a decision.
/// Obtain decision timestamps from trusted execution. Preserve work order, revision, final reviewed
/// scope, actor, decision and time in the audit record; associate the run through trusted state.
/// Requests must be non-null. Failed operations must not partially mutate persisted state.
/// Expected business failures use typed outcomes. Invalid constructor arguments are programming
/// errors; cancellation propagates as OperationCanceledException and technical failures as exceptions.
/// Cancellation does not mean human rejection or durable run cancellation, nor guarantee rollback:
/// after an uncertain outcome reread state before retrying; do not append duplicate decisions.
/// </remarks>
public interface IWorkOrderApprovalService
{
    /// <summary>Reads one authorized persisted snapshot; null means not found or not accessible.</summary>
    /// <remarks>WorkOrderId must be nonempty and actorId nonblank, sourced from trusted host context.</remarks>
    Task<WorkOrderReviewSnapshot?> GetReviewAsync(Guid workOrderId, string actorId, CancellationToken cancellationToken);

    /// <summary>Submits an existing draft with a current authoritative assessment for supervisor review.</summary>
    /// <remarks>
    /// Trusted validation must already have established the draft's executable content and complete
    /// authoritative requirements; an agent proposal or empty advisory list is not that assessment.
    /// Applied returns PendingApproval at the same revision with a new concurrency token.
    /// </remarks>
    Task<ApprovalOperationResult> SubmitForReviewAsync(SubmitWorkOrderReviewRequest request, CancellationToken cancellationToken);

    /// <summary>Records approve, reject, or edit-and-approve intent against a pending review target.</summary>
    /// <remarks>
    /// Approve/Reject apply to the current scope without changing revision. EditAndApprove carries
    /// the FINAL content and authoritative requirement definitions already presented for consent.
    /// Execution must establish that this exact scope came from trusted deterministic validation
    /// and is the scope the supervisor consented to, not trust client/model assertions or DTO names.
    /// Do not silently recompute or alter it after consent: any material change requires renewed review.
    /// Pass that exact scope to Domain EditAndApprove, producing revision N+1 and clearing verification.
    /// Applied returns the corresponding Approved/Rejected state and a new concurrency token.
    /// Already decided targets return InvalidState (or Conflict when stale). SafetyValidationFailed
    /// and InvalidEdit leave state unchanged. Approval never satisfies prerequisites or dispatches work.
    /// Requests must be non-null. Forbidden represents denied authorization without disclosing state.
    /// </remarks>
    Task<ApprovalOperationResult> RecordDecisionAsync(RecordWorkOrderDecisionRequest request, CancellationToken cancellationToken);
}
