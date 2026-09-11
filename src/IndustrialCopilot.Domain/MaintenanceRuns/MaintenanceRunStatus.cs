namespace IndustrialCopilot.Domain.MaintenanceRuns;

public enum MaintenanceRunStatus
{
    Queued = 1,
    Running = 2,
    WaitingForApproval = 3,
    Cancelled = 4,
    Failed = 5,
    Completed = 6
}
