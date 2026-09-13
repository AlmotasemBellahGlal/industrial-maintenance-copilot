namespace IndustrialCopilot.Domain.MaintenanceRuns;

public sealed class MaintenanceRun
{
    public Guid Id { get; }
    public Guid EquipmentId { get; }
    public string ReportedSymptom { get; }
    public MaintenanceRunStatus Status { get; private set; } = MaintenanceRunStatus.Queued;
    public bool IsCancellationRequested { get; private set; }

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
        EnsureCancellationNotRequested();
        Status = MaintenanceRunStatus.Running;
    }

    /// <summary>Restores a validated durable lifecycle snapshot without replay.</summary>
    public static MaintenanceRun Restore(Guid id, Guid equipmentId, string reportedSymptom,
        MaintenanceRunStatus status, bool isCancellationRequested)
    {
        if (!Enum.IsDefined(status) || (status == MaintenanceRunStatus.Cancelled && !isCancellationRequested)
            || (status == MaintenanceRunStatus.Completed && isCancellationRequested))
            throw new ArgumentException("Inconsistent run lifecycle.");
        return new(id, equipmentId, reportedSymptom) { Status = status, IsCancellationRequested = isCancellationRequested };
    }

    public void WaitForApproval()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        EnsureCancellationNotRequested();
        Status = MaintenanceRunStatus.WaitingForApproval;
    }

    public void Resume()
    {
        EnsureStatus(MaintenanceRunStatus.WaitingForApproval);
        EnsureCancellationNotRequested();
        Status = MaintenanceRunStatus.Running;
    }

    public void Complete()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        EnsureCancellationNotRequested();
        Status = MaintenanceRunStatus.Completed;
    }

    public void RequestCancellation()
    {
        EnsureActive();
        EnsureCancellationNotRequested();
        IsCancellationRequested = true;
    }

    // The caller acknowledges cancellation only after reaching a safe execution boundary.
    public void AcknowledgeCancellation()
    {
        EnsureActive();
        if (!IsCancellationRequested) throw new InvalidOperationException("Cancellation must be requested before acknowledgement.");
        Status = MaintenanceRunStatus.Cancelled;
    }

    public void Fail()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        Status = MaintenanceRunStatus.Failed;
    }

    public void Block()
    {
        EnsureStatus(MaintenanceRunStatus.Running);
        Status = MaintenanceRunStatus.Blocked;
    }

    private void EnsureActive()
    {
        if (Status is not (MaintenanceRunStatus.Queued or MaintenanceRunStatus.Running or MaintenanceRunStatus.WaitingForApproval))
            throw new InvalidOperationException("Run must be active for this operation.");
    }

    private void EnsureCancellationNotRequested()
    {
        if (IsCancellationRequested) throw new InvalidOperationException("Cancellation has already been requested.");
    }

    private void EnsureStatus(MaintenanceRunStatus expected)
    {
        if (Status != expected) throw new InvalidOperationException($"Run must be {expected} for this operation.");
    }
}
