using IndustrialCopilot.Application.Abstractions.Agents;
namespace IndustrialCopilot.Application.Reasoning;
public enum MaintenanceProgressKind { WorkflowStarted,AgentStarted,AgentCompleted,SafetyEvaluated,WorkOrderReady,WaitingForApproval,Blocked,Cancelled,Failed,RetryScheduled,FallbackStarted,FallbackCompleted }
/// <summary>Safe execution observation; no model payloads or business authority.</summary>
public sealed record MaintenanceProgress(MaintenanceProgressKind Kind,Guid RunId,AgentRole? Role=null,bool? Allowed=null,Guid? WorkOrderId=null,string? ReasonCode=null,int? Attempt=null);
