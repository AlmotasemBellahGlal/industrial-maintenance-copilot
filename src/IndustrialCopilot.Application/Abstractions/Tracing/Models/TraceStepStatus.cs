namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>Observed execution status only; never a Domain lifecycle transition or authorization.</summary>
public enum TraceStepStatus { Running = 1, Waiting = 2, Completed = 3, Failed = 4, Cancelled = 5 }
