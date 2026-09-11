using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Abstractions.Approval.Models;

/// <summary>The final edited scope presented for human consent; construction does not prove review or safety completeness.</summary>
/// <remarks>
/// Trusted execution must bind this exact content and requirement set to the supervisor's consent.
/// Empty requirements mean an explicitly established empty assessment, never missing assessment.
/// Verification is excluded because it cannot transfer to edited work.
/// </remarks>
public sealed record ReviewedWorkOrderScope
{
    public WorkOrderContent Content { get; }
    public IReadOnlyList<SafetyPrerequisite> SafetyPrerequisites { get; }

    public ReviewedWorkOrderScope(WorkOrderContent content, IReadOnlyList<SafetyPrerequisite> safetyPrerequisites)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(safetyPrerequisites);
        var snapshot = safetyPrerequisites.ToArray();
        if (snapshot.Any(p => p is null) || snapshot.Select(p => p.Id).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Requirements must have unique identities and no null entries.", nameof(safetyPrerequisites));
        if (snapshot.Any(p => p.Verification is not null))
            throw new ArgumentException("Edited scope must not carry prior verification.", nameof(safetyPrerequisites));
        Content = content;
        SafetyPrerequisites = Array.AsReadOnly(snapshot);
    }
}
