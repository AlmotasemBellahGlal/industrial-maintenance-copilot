using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

namespace IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;

// The trusted host must review the plan deterministically before invoking generation.
public sealed record WorkOrderGenerationInput
{
    public string ReportedSymptom { get; }
    public DiagnosticPlan Plan { get; }

    public WorkOrderGenerationInput(string reportedSymptom, DiagnosticPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        ArgumentNullException.ThrowIfNull(plan);
        ReportedSymptom = reportedSymptom;
        Plan = plan;
    }
}
