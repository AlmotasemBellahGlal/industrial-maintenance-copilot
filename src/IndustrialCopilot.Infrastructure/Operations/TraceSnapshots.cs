using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;

namespace IndustrialCopilot.Infrastructure.Operations;

/// <summary>Explicit document shape excludes derived root and aggregate accounting properties.</summary>
internal sealed record TraceDocument(Guid ExecutionId,Guid CorrelationId,Guid? MaintenanceRunId,long Version,TraceStep[] Steps)
{
    internal static TraceDocument From(RunTraceSnapshot s) => new(s.ExecutionId,s.CorrelationId,s.MaintenanceRunId,s.Version,s.Steps.OrderBy(x=>x.StepId).ToArray());
    internal RunTraceSnapshot Restore() => new(ExecutionId,CorrelationId,MaintenanceRunId,Version,Steps);
}

internal static class TraceSnapshots
{
    internal static string Serialize(RunTraceSnapshot snapshot) => JsonSerializer.Serialize(TraceDocument.From(snapshot));
    internal static RunTraceSnapshot Deserialize(string json) =>
        (JsonSerializer.Deserialize<TraceDocument>(json) ?? throw new ArgumentException("Missing trace snapshot.")).Restore();
    internal static bool Equivalent(RunTraceSnapshot a,RunTraceSnapshot b) =>
        a.ExecutionId==b.ExecutionId && a.CorrelationId==b.CorrelationId && a.MaintenanceRunId==b.MaintenanceRunId && a.Version==b.Version
        && a.Steps.OrderBy(s=>s.StepId).SequenceEqual(b.Steps.OrderBy(s=>s.StepId));

    internal static void ValidateUpdate(RunTraceSnapshot old,RunTraceSnapshot next)
    {
        if(old.ExecutionId!=next.ExecutionId || old.CorrelationId!=next.CorrelationId ||
            (old.MaintenanceRunId is not null && old.MaintenanceRunId!=next.MaintenanceRunId))
            throw new ArgumentException("Trace identity cannot change.");
        var steps=next.Steps.ToDictionary(s=>s.StepId);
        foreach(var previous in old.Steps)
        {
            if(!steps.TryGetValue(previous.StepId,out var step) || previous.ParentStepId!=step.ParentStepId ||
                previous.Kind!=step.Kind || previous.StartedAt!=step.StartedAt || previous.OperationName!=step.OperationName ||
                previous.AgentRole!=step.AgentRole)
                throw new ArgumentException("Existing trace observations cannot disappear or change identity.");
            if(previous.CompletedAt is not null && (previous.Status!=step.Status || previous.CompletedAt!=step.CompletedAt || previous.Error!=step.Error))
                throw new ArgumentException("Terminal observations cannot be rewritten.");
            if((previous.Usage is not null && previous.Usage!=step.Usage) || (previous.Cost is not null && previous.Cost!=step.Cost)
                || (previous.WorkOrderId is not null && (previous.WorkOrderId!=step.WorkOrderId || previous.WorkOrderRevision!=step.WorkOrderRevision)))
                throw new ArgumentException("Existing accounting and approval observations cannot be overwritten.");
        }
    }
}
