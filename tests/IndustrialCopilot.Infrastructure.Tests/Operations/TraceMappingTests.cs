using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Infrastructure.Operations;

namespace IndustrialCopilot.Infrastructure.Tests.Operations;

public class TraceMappingTests
{
    private static readonly DateTimeOffset Start=DateTimeOffset.Parse("2026-09-13T12:00:00.1234567+02:00");
    private static TraceStep Step(Guid id,TraceStepStatus status=TraceStepStatus.Running,TokenUsage? usage=null,MonetaryCost? cost=null) =>
        new(id,null,TraceOperationKind.Llm,"embedding",Start,status,status==TraceStepStatus.Running?null:Start.AddSeconds(1),
            usage:usage,cost:cost);
    [Fact]
    public void JsonRoundTripUsesValidatedConstructorsAndPreservesPrecisionAndAccounting()
    {
        var step=Step(Guid.NewGuid(),TraceStepStatus.Completed,new(2,3,5),new(0.012345m,"USD"));
        var snapshot=new RunTraceSnapshot(Guid.NewGuid(),Guid.NewGuid(),null,2,[step]);
        var restored=TraceSnapshots.Deserialize(TraceSnapshots.Serialize(snapshot));
        Assert.True(TraceSnapshots.Equivalent(snapshot,restored));
        Assert.Equal(Start.Offset,restored.Root.StartedAt.Offset);
        Assert.Equal(snapshot.Root.Cost,restored.Root.Cost);
        Assert.Throws<ArgumentOutOfRangeException>(()=>TraceSnapshots.Deserialize(TraceSnapshots.Serialize(snapshot).Replace("\"Version\":2","\"Version\":0")));
    }

    [Fact]
    public void AllowsCompletionAndLateAccountingButRejectsLostOrRewrittenObservations()
    {
        var id=Guid.NewGuid(); var execution=Guid.NewGuid(); var correlation=Guid.NewGuid();
        var active=new RunTraceSnapshot(execution,correlation,null,1,[Step(id)]);
        var completed=new RunTraceSnapshot(execution,correlation,Guid.NewGuid(),2,[Step(id,TraceStepStatus.Completed)]);
        TraceSnapshots.ValidateUpdate(active,completed);
        var accounted=new RunTraceSnapshot(execution,correlation,completed.MaintenanceRunId,3,[Step(id,TraceStepStatus.Completed,new(1,2,3),new(1m,"USD"))]);
        TraceSnapshots.ValidateUpdate(completed,accounted);
        Assert.Throws<ArgumentException>(()=>TraceSnapshots.ValidateUpdate(accounted,completed));
        Assert.Throws<ArgumentException>(()=>TraceSnapshots.ValidateUpdate(completed,active));
        Assert.Throws<ArgumentException>(()=>TraceSnapshots.ValidateUpdate(active,new(execution,correlation,null,2,[Step(Guid.NewGuid())])));
        Assert.Throws<ArgumentException>(()=>TraceSnapshots.ValidateUpdate(active,new(execution,Guid.NewGuid(),null,2,[Step(id)])));
    }
}
