namespace IndustrialCopilot.Application.Abstractions.Agents;

/// <summary>Describes the result of one agent invocation.</summary>
/// <remarks>
/// No outcome represents safety approval, human rejection, a MaintenanceRun lifecycle state,
/// or permission to dispatch work.
/// </remarks>
public enum AgentOutcome
{
    /// <summary>A usable agent proposal or result was produced.</summary>
    Success = 1,
    /// <summary>Grounding is insufficient to produce a usable result.</summary>
    InsufficientEvidence = 2,
    /// <summary>The agent cannot produce a usable result for another reason.</summary>
    CannotProceed = 3
}
