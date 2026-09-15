using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Jobs;
using IndustrialCopilot.Application.Reasoning;

namespace IndustrialCopilot.Application.Tests.Reasoning;

public class ReasoningJobProcessorTests
{
    internal sealed class Jobs: IReasoningJobStore
    {
        public JobLeaseState State=JobLeaseState.Owned;
        public bool Released; public bool Completed;
        public Task<JobLeaseState> RenewAsync(ReasoningJobClaim c,TimeSpan l,CancellationToken ct)=>Task.FromResult(State);
        public Task ReleaseAsync(ReasoningJobClaim c,CancellationToken ct){Released=true;return Task.CompletedTask;}
        public Task CompleteAsync(ReasoningJobClaim c,MaintenanceReasoningResult? r,CancellationToken ct){Completed=true;return Task.CompletedTask;}
        public Task ReportAsync(ReasoningJobClaim c,MaintenanceProgress p,CancellationToken ct)=>Task.CompletedTask;
        public Task<ReasoningJobSnapshot> SubmitAsync(ReasoningJobSubmission s,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ReasoningJobSnapshot?> GetAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ReasoningJobSnapshot?> RequestCancellationAsync(Guid id,CancellationToken ct)=>throw new NotSupportedException();
        public Task<ReasoningJobClaim?> ClaimAsync(string o,IReadOnlyCollection<Guid> e,TimeSpan l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IReadOnlyList<ReasoningJobEvent>> ReadProgressAsync(Guid id,long a,int n,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Scope:IReasoningJobExecutionScope,IDisposable
    {public IDisposable Enter(ReasoningJobClaim c)=>this;public void Dispose(){} }
    [Theory]
    [InlineData(JobLeaseState.CancellationRequested,false)]
    [InlineData(JobLeaseState.Lost,false)]
    [InlineData(JobLeaseState.Owned,true)]
    public async Task CancellationOwnershipLossAndShutdownReachProviderAndReleaseInsteadOfFalseCompletion(JobLeaseState state,bool shutdown)
    {
        var s=new Scenario();var jobs=new Jobs();using var stop=new CancellationTokenSource();
        await s.Store.TrySaveRunAsync(new(s.Request.RunId,s.Candidate.EquipmentId,s.Request.Input.ReportedSymptom),null,default);
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancelled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Llm.Script.Enqueue(async(_,ct)=>{entered.SetResult();try{await Task.Delay(Timeout.Infinite,ct);}catch(OperationCanceledException){cancelled.TrySetResult();throw;}throw new InvalidOperationException();});
        var processor=new ReasoningJobProcessor(jobs,s.Orchestrator(),new Scope());
        var execute=processor.ExecuteAsync(new(Guid.NewGuid(),Guid.NewGuid(),"worker",1,"actor",s.Request),TimeSpan.FromSeconds(1),stop.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));jobs.State=state;if(shutdown)stop.Cancel();
        await execute.WaitAsync(TimeSpan.FromSeconds(5));await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(jobs.Released);Assert.False(jobs.Completed);Assert.Equal(0,s.Store.Publishes);
        Assert.False((await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.IsCancellationRequested);
    }
}
