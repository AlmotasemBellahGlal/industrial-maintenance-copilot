using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

namespace IndustrialCopilot.Application.Abstractions.Workflow;

public sealed record StoredWorkOrder(WorkOrder Order, string ConcurrencyToken, Guid? MaintenanceRunId);
public sealed record StoredMaintenanceRun(MaintenanceRun Run, string ConcurrencyToken);

/// <summary>Trusted workflow storage, not an authorization or dispatch capability.</summary>
public interface IWorkflowStore
{
    Task<StoredWorkOrder?> GetWorkOrderAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Optimistic save. Outstanding Pending/Uncertain dispatch reservations reject mutations.
    /// Ordinary saves cannot create Dispatched state or rewrite a dispatched order; confirmed external
    /// acceptance is committed through IDispatchAttemptStore. Detached Domain mutation is not persistence.</summary>
    Task<string?> TrySaveWorkOrderAsync(WorkOrder order, Guid? runId, string? expectedToken, CancellationToken cancellationToken);
    Task<StoredMaintenanceRun?> GetRunAsync(Guid id, CancellationToken cancellationToken);
    /// <summary>Rejects changes to runs bound to outstanding dispatch reservations, including cancellation intent.</summary>
    Task<string?> TrySaveRunAsync(MaintenanceRun run, string? expectedToken, CancellationToken cancellationToken);
    /// <summary>
    /// Atomically creates a pending, unapproved work order and changes a Running run to WaitingForApproval.
    /// False means stale run token, cancellation intent, invalid current lifecycle, or existing order;
    /// neither aggregate is changed. Tokens are opaque and distinct from Domain Revision.
    /// </summary>
    Task<bool> TryPublishReviewAsync(WorkOrder order, MaintenanceRun run, string expectedRunToken, WorkOrderProposal proposal, CancellationToken cancellationToken);
    /// <summary>Reads the original grounded proposal, an observation rather than current approval authority.</summary>
    Task<WorkOrderProposal?> GetProposalAsync(Guid workOrderId, CancellationToken cancellationToken);
}
