using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>Immutable observation of one operation/attempt; not a command, approval, or tool permission.</summary>
/// <remarks>
/// OperationName is a safe application-owned name, not a prompt, argument payload or provider response.
/// Each Llm step is one billable attempt, including failed/cancelled attempts with incurred usage.
/// Record usage only on Llm steps, never again on parent agent/orchestration steps.
/// Waiting on an Approval operation observes a boundary; completion does not imply approval was granted.
/// </remarks>
public sealed record TraceStep
{
    public Guid StepId { get; }
    public Guid? ParentStepId { get; }
    public TraceOperationKind Kind { get; }
    public string OperationName { get; }
    public DateTimeOffset StartedAt { get; }
    public DateTimeOffset? CompletedAt { get; }
    public TimeSpan? Duration => CompletedAt - StartedAt;
    public TraceStepStatus Status { get; }
    public TraceError? Error { get; }
    public AgentRole? AgentRole { get; }
    public TokenUsage? Usage { get; }
    public MonetaryCost? Cost { get; }
    public Guid? WorkOrderId { get; }
    public int? WorkOrderRevision { get; }

    public TraceStep(Guid stepId, Guid? parentStepId, TraceOperationKind kind, string operationName,
        DateTimeOffset startedAt, TraceStepStatus status, DateTimeOffset? completedAt = null,
        TraceError? error = null, AgentRole? agentRole = null, TokenUsage? usage = null,
        MonetaryCost? cost = null, Guid? workOrderId = null, int? workOrderRevision = null)
    {
        if (stepId == Guid.Empty) throw new ArgumentException("Step identity is required.", nameof(stepId));
        if (parentStepId == Guid.Empty || parentStepId == stepId) throw new ArgumentException("Invalid parent identity.", nameof(parentStepId));
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        if (startedAt == default) throw new ArgumentException("Start time is required.", nameof(startedAt));
        var terminal = status is TraceStepStatus.Completed or TraceStepStatus.Failed or TraceStepStatus.Cancelled;
        if (terminal != completedAt.HasValue || completedAt < startedAt)
            throw new ArgumentException("Terminal steps need an end at or after start; active steps cannot have an end.", nameof(completedAt));
        if ((status == TraceStepStatus.Failed) != (error is not null))
            throw new ArgumentException("Only failed steps require error information.", nameof(error));
        if (agentRole.HasValue && (!Enum.IsDefined(agentRole.Value) || kind != TraceOperationKind.Agent))
            throw new ArgumentException("Agent role is only valid on agent operations.", nameof(agentRole));
        if (kind == TraceOperationKind.Agent && agentRole is null)
            throw new ArgumentException("Agent operations require a role.", nameof(agentRole));
        if (kind != TraceOperationKind.Llm && (usage is not null || cost is not null))
            throw new ArgumentException("Accounting belongs only to individual Llm attempts.");
        if (usage is not null && (long)usage.PromptTokens + usage.CompletionTokens != usage.TotalTokens)
            throw new ArgumentException("Token total must equal prompt plus completion tokens.", nameof(usage));
        if (workOrderId == Guid.Empty || workOrderRevision <= 0 || workOrderId.HasValue != workOrderRevision.HasValue)
            throw new ArgumentException("Work order identity and positive revision must be supplied together.");
        StepId = stepId;
        ParentStepId = parentStepId;
        Kind = kind;
        OperationName = operationName;
        StartedAt = startedAt;
        Status = status;
        CompletedAt = completedAt;
        Error = error;
        AgentRole = agentRole;
        Usage = usage;
        Cost = cost;
        WorkOrderId = workOrderId;
        WorkOrderRevision = workOrderRevision;
    }
}
