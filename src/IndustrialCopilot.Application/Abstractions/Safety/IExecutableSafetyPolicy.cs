using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Abstractions.Safety;

/// <summary>Assesses exact executable scope from trusted procedures, independently of agent plans.</summary>
public interface IExecutableSafetyPolicy
{
    Task<SafetyAssessment> AssessAsync(WorkOrderContent content,CancellationToken cancellationToken);
}
