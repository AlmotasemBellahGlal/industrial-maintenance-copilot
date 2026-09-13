namespace IndustrialCopilot.Application.Abstractions.Workflow;
public sealed record RunLinks(IReadOnlyList<Guid> WorkOrderIds,IReadOnlyList<Guid> ExecutionIds);
public interface IWorkflowInspection { Task<RunLinks> GetLinksAsync(Guid runId,CancellationToken cancellationToken); }
