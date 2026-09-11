namespace IndustrialCopilot.Domain.MaintenanceRuns;

public sealed class MaintenanceRun
{
    public Guid Id { get; }
    public Guid EquipmentId { get; }
    public string ReportedSymptom { get; }
    public MaintenanceRunStatus Status { get; private set; } = MaintenanceRunStatus.Queued;

    public MaintenanceRun(Guid id, Guid equipmentId, string reportedSymptom)
    {
        if (id == Guid.Empty) throw new ArgumentException("Run identity is required.", nameof(id));
        if (equipmentId == Guid.Empty) throw new ArgumentException("Equipment identity is required.", nameof(equipmentId));
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedSymptom);
        Id = id;
        EquipmentId = equipmentId;
        ReportedSymptom = reportedSymptom;
    }

    public void Start()
    {
        EnsureStatus(MaintenanceRunStatus.Queued);
        Status = MaintenanceRunStatus.Running;
    }

    public void WaitForApproval()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        Status = MaintenanceRunStatus.WaitingForApproval;
    }

    public void Resume()
    {
        EnsureStatus(MaintenanceRunStatus.WaitingForApproval);
        Status = MaintenanceRunStatus.Running;
    }

    public void Complete()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        Status = MaintenanceRunStatus.Completed;
    }

    public void Cancel()
    {
        if (Status is not (MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running or MaintenanceRunStatus.WaitingForApproval))
            throw new InvalidOperationException("Only an active run can be cancelled.");
        Status = MaintenanceRunStatus.Cancelled;
    }

    public void Fail()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        Status = MaintenanceRunStatus.Failed;
    }

    private void EnsureStatus(MaintenanceRunStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Run must be {expected} for this operation.");
    }
}
