namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

/// <summary>Operation handling outcomes, not safety verification, dispatch permission, or run lifecycle states.</summary>
public enum ApprovalOperationOutcome
{
    Applied = 1,
    NotFound = 2,
    Conflict = 3,
    InvalidState = 4,
    SafetyValidationFailed = 5,
    InvalidEdit = 6,
    Forbidden = 7
}
