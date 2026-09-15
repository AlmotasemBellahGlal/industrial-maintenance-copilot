using IndustrialCopilot.Application.Reasoning;

namespace IndustrialCopilot.Application.Abstractions.Jobs;

// Scheduling state only. Succeeded means reasoning finished, not human approval or dispatch.
public enum ReasoningJobStatus { Queued=1, Running=2, Succeeded=3, Failed=4, Cancelled=5 }
public enum ReasoningJobPhase { Queued, Starting, MatchingSymptoms, PlanningDiagnostics, ValidatingSafety, GeneratingWorkOrder, WaitingForApproval, Blocked, Failed, Cancelled, Recovering }
public sealed record ReasoningJobAttempt(int Number, Guid ExecutionId, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, string? FailureCode);
public sealed record ReasoningJobSnapshot(Guid JobId, Guid MaintenanceRunId, Guid EquipmentId, Guid CorrelationId,
    ReasoningJobStatus Status, ReasoningJobPhase Phase, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    bool CancellationRequested, long Version, IReadOnlyList<ReasoningJobAttempt> Attempts, MaintenanceReasoningResult? Result, string? FailureCode)
{
    public bool IsTerminal => Status is ReasoningJobStatus.Succeeded or ReasoningJobStatus.Failed or ReasoningJobStatus.Cancelled;
}
public sealed class ReasoningJobSubmission
{
    public string ActorId { get; }
    public string IdempotencyKey { get; }
    public MaintenanceReasoningRequest Request { get; }
    public ReasoningJobSubmission(string actorId,string idempotencyKey,MaintenanceReasoningRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(request);
        if(actorId.Length>100 || idempotencyKey.Length>128 || idempotencyKey.Any(c=>!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '.' or ':')))
            throw new ArgumentException("Invalid submission identity.");
        ActorId=actorId; IdempotencyKey=idempotencyKey; Request=request;
    }
}
public sealed record ReasoningJobEvent(long Sequence,Guid ExecutionId,MaintenanceProgress Progress);
public sealed record ReasoningJobClaim(Guid JobId, Guid LeaseToken, string Owner, int Attempt, string ActorId, MaintenanceReasoningRequest Request);
public sealed class JobSubmissionConflictException() : Exception("Idempotency key is already bound to a different request.");
public sealed class JobLeaseLostException() : Exception("Reasoning job ownership is no longer valid.");
public sealed class JobCancellationConflictException() : Exception("Reasoning has already reached its terminal business boundary.");
public enum JobLeaseState { Owned, CancellationRequested, Lost }
public interface IReasoningJobStore
{
    Task<ReasoningJobSnapshot> SubmitAsync(ReasoningJobSubmission submission,CancellationToken cancellationToken);
    Task<ReasoningJobSnapshot?> GetAsync(Guid jobId,CancellationToken cancellationToken);
    Task<ReasoningJobSnapshot?> RequestCancellationAsync(Guid jobId,CancellationToken cancellationToken);
    Task<ReasoningJobClaim?> ClaimAsync(string owner,IReadOnlyCollection<Guid> equipmentIds,TimeSpan lease,CancellationToken cancellationToken);
    Task<JobLeaseState> RenewAsync(ReasoningJobClaim claim,TimeSpan lease,CancellationToken cancellationToken);
    Task<IReadOnlyList<ReasoningJobEvent>> ReadProgressAsync(Guid jobId,long after,int limit,CancellationToken cancellationToken);
    Task ReportAsync(ReasoningJobClaim claim,MaintenanceProgress progress,CancellationToken cancellationToken);
    Task CompleteAsync(ReasoningJobClaim claim,MaintenanceReasoningResult? result,CancellationToken cancellationToken);
    Task ReleaseAsync(ReasoningJobClaim claim,CancellationToken cancellationToken);
}
/// <summary>Trusted host binds a claim to current actor permissions and persistence fencing. Never accepts client authority.</summary>
public interface IReasoningJobExecutionScope
{
    IDisposable Enter(ReasoningJobClaim claim);
}
