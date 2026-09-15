using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Domain.MaintenanceRuns;

namespace IndustrialCopilot.Application.Tests.Reasoning;

public class DurableAttemptTests
{
    [Fact]
    public async Task ShutdownLeavesBusinessRunRecoverableRatherThanClaimingUserCancellation()
    {
        var s=new Scenario();
        await s.Store.TrySaveRunAsync(new(s.Request.RunId,s.Candidate.EquipmentId,s.Request.Input.ReportedSymptom),null,default);
        using var shutdown=new CancellationTokenSource();
        s.Llm.Script.Enqueue(async(_,ct)=>{shutdown.Cancel();await Task.Delay(Timeout.Infinite,ct);throw new InvalidOperationException();});
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.Orchestrator().ExecuteAsync(s.Request,shutdown.Token,durableAttempt:true));
        var run=(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run;
        Assert.Equal(MaintenanceRunStatus.Running,run.Status);Assert.False(run.IsCancellationRequested);Assert.Equal(0,s.Store.Publishes);
    }
    [Fact]
    public async Task LostLeaseIsNotSwallowedOrConvertedIntoBusinessFailure()
    {
        var s=new Scenario();s.Success();
        await s.Store.TrySaveRunAsync(new(s.Request.RunId,s.Candidate.EquipmentId,s.Request.Input.ReportedSymptom),null,default);
        await Assert.ThrowsAsync<JobLeaseLostException>(()=>s.Orchestrator().ExecuteAsync(s.Request,default,durableAttempt:true,
            durableProgress:(p,_)=>p.Kind==IndustrialCopilot.Application.Reasoning.MaintenanceProgressKind.AgentStarted?throw new JobLeaseLostException():Task.CompletedTask));
        Assert.Equal(0,s.Llm.Calls);Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(MaintenanceRunStatus.Running,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
    }
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("key with space")]
    [InlineData("key\nheader")]
    public void SubmissionRejectsInvalidIdempotencyKeys(string key)
    {Assert.Throws<ArgumentException>(()=>new ReasoningJobSubmission("actor",key,new Scenario().Request));}
}
