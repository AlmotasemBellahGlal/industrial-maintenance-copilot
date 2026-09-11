namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>A complete execution observation at one storage version. The root step describes execution, not Domain lifecycle.</summary>
public sealed record RunTraceSnapshot
{
    /// <summary>One execution attempt/session; retries or resumed sessions may have separate IDs.</summary>
    public Guid ExecutionId { get; }
    /// <summary>Stable across related executions, agent/tool calls, and human interactions.</summary>
    public Guid CorrelationId { get; }
    /// <summary>Optional trusted Domain association, allowing pre-equipment matching to be traced.</summary>
    public Guid? MaintenanceRunId { get; }
    public long Version { get; }
    public IReadOnlyList<TraceStep> Steps { get; }
    public TraceStep Root { get; }
    public RunUsageSummary Usage => RunUsageSummary.Aggregate(Steps);

    public RunTraceSnapshot(Guid executionId, Guid correlationId, Guid? maintenanceRunId,
        long version, IReadOnlyList<TraceStep> steps)
    {
        if (executionId == Guid.Empty) throw new ArgumentException("Execution identity is required.", nameof(executionId));
        if (correlationId == Guid.Empty) throw new ArgumentException("Correlation identity is required.", nameof(correlationId));
        if (maintenanceRunId == Guid.Empty) throw new ArgumentException("Run identity cannot be empty.", nameof(maintenanceRunId));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        ArgumentNullException.ThrowIfNull(steps);
        var snapshot = steps.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(s => s is null) || snapshot.Select(s => s.StepId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("A nonempty collection of unique, non-null steps is required.", nameof(steps));
        var roots = snapshot.Where(s => s.ParentStepId is null).ToArray();
        if (roots.Length != 1) throw new ArgumentException("Exactly one root step is required.", nameof(steps));
        var byId = snapshot.ToDictionary(s => s.StepId);
        foreach (var step in snapshot)
        {
            var visited = new HashSet<Guid> { step.StepId };
            var current = step;
            while (current.ParentStepId is { } parentId)
            {
                if (!byId.TryGetValue(parentId, out var parent) || !visited.Add(parentId))
                    throw new ArgumentException("Parents must exist and cycles are not allowed.", nameof(steps));
                if (current.StartedAt < parent.StartedAt || (parent.CompletedAt is { } end &&
                    (current.CompletedAt is null || current.CompletedAt > end)))
                    throw new ArgumentException("Child timing must fit inside its parent's interval.", nameof(steps));
                current = parent;
            }
        }
        ExecutionId = executionId;
        CorrelationId = correlationId;
        MaintenanceRunId = maintenanceRunId;
        Version = version;
        Steps = Array.AsReadOnly(snapshot);
        Root = roots[0];
    }
}
