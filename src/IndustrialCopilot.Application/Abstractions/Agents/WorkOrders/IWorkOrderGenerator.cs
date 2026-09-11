namespace IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

// Trusted host owns role/tool authorization and evidence resolution.
// Cancellation propagates as OperationCanceledException; technical failures remain exceptions.
public interface IWorkOrderGenerator
{
    Task<WorkOrderGenerationResult> GenerateAsync(WorkOrderGenerationInput input, CancellationToken cancellationToken);
}
