using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

namespace IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;

public sealed record DiagnosticPlanInput
{
    public string ReportedSymptom { get; }
    public SymptomMatch Match { get; }

    public DiagnosticPlanInput(string reportedSymptom, SymptomMatch match)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        ArgumentNullException.ThrowIfNull(match);
        ReportedSymptom = reportedSymptom;
        Match = match;
    }
}
