using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

namespace IndustrialCopilot.Application.Reasoning;

public sealed class MaintenanceReasoningRequest
{
    public Guid ExecutionId { get; }
    public Guid CorrelationId { get; }
    public Guid RunId { get; }
    public Guid WorkOrderId { get; }
    public SymptomMatchInput Input { get; }
    public MaintenanceReasoningRequest(Guid executionId,Guid correlationId,Guid runId,Guid workOrderId,SymptomMatchInput input)
    {
        if(executionId==Guid.Empty || correlationId==Guid.Empty || runId==Guid.Empty || workOrderId==Guid.Empty) throw new ArgumentException("Workflow identities required.");
        ArgumentNullException.ThrowIfNull(input);
        if(input.Candidates.Select(c=>c.EquipmentId).Distinct().Count()!=1) throw new ArgumentException("A maintenance run targets one equipment identity.");
        ExecutionId=executionId; CorrelationId=correlationId; RunId=runId; WorkOrderId=workOrderId; Input=input;
    }
}
public enum MaintenanceReasoningOutcome { Proposed=1, InsufficientEvidence=2, CannotProceed=3, TimedOut=4, Failed=5, Conflict=6, Cancelled=7 }
public sealed record MaintenanceReasoningResult(MaintenanceReasoningOutcome Outcome, Guid RunId, Guid? WorkOrderId, bool TraceComplete = true);
