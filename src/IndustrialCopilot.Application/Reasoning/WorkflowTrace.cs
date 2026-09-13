using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;

namespace IndustrialCopilot.Application.Reasoning;

internal sealed class WorkflowTrace(IRunTraceStore store, Guid execution, Guid correlation, Guid run, TimeProvider clock)
{
    private readonly object gate = new();
    private readonly List<TraceStep> steps = [];
    private long version;
    internal Guid Start(TraceOperationKind kind, string name, Guid? parent = null, AgentRole? role = null)
    {
        lock (gate)
        {
            if (parent is not null && steps.Single(s => s.StepId == parent).CompletedAt is not null) throw new OperationCanceledException();
            var id = Guid.NewGuid();
            steps.Add(new(id,parent,kind,name,clock.GetUtcNow(),TraceStepStatus.Running,agentRole:role));
            return id;
        }
    }
    internal void End(Guid id, TraceStepStatus status = TraceStepStatus.Completed, string? code = null, TokenUsage? usage = null,
        Guid? workOrder = null, int? revision = null)
    {
        lock (gate)
        {
            var index = steps.FindIndex(s => s.StepId == id); var step = steps[index];
            if (step.CompletedAt is not null) return; // Ignore late provider responses after timeout.
            foreach (var child in steps.Where(s => s.ParentStepId == id && s.CompletedAt is null).ToArray())
                End(child.StepId,status,code);
            var end = clock.GetUtcNow();
            var lastChildEnd = steps.Where(s => s.ParentStepId == id).Select(s => s.CompletedAt ?? s.StartedAt).DefaultIfEmpty(step.StartedAt).Max();
            if (end < lastChildEnd) end = lastChildEnd;
            steps[index] = new(step.StepId,step.ParentStepId,step.Kind,step.OperationName,step.StartedAt,status,end,
                status == TraceStepStatus.Failed ? new TraceError(code ?? "execution_failed","Execution could not continue.") : null,
                step.AgentRole,usage,workOrderId:workOrder,workOrderRevision:revision);
        }
    }
    internal async Task Flush(CancellationToken token)
    {
        RunTraceSnapshot snapshot;
        lock (gate) snapshot = new(execution,correlation,run,checked(version+1),steps.ToArray());
        if (!await store.TrySaveAsync(snapshot,version,token)) throw new WorkflowConflictException();
        version = snapshot.Version;
    }
}
public sealed class WorkflowConflictException() : Exception("Workflow state changed concurrently.");
