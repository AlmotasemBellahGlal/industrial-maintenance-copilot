using IndustrialCopilot.Application.Abstractions.Jobs;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Jobs;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Infrastructure.Knowledge;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using IndustrialCopilot.IntegrationTests.Operations;

namespace IndustrialCopilot.IntegrationTests.Reasoning;

public class ReasoningJobTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    private PostgresReasoningJobStore Jobs=>new(database.Source);
    private async Task<MaintenanceReasoningRequest> Request(string culture="en-US")
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var equipment=Guid.NewGuid();
        await using var cmd=database.Source.CreateCommand("INSERT INTO operations.equipment VALUES(@id)");cmd.Parameters.AddWithValue("id",equipment);await cmd.ExecuteNonQueryAsync();
        return new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("vibration",[new(equipment,"pump",Guid.NewGuid(),Guid.NewGuid())],[]),culture);
    }
    private Task<ReasoningJobClaim?> Claim(MaintenanceReasoningRequest r)=>Jobs.ClaimAsync(Guid.NewGuid().ToString(),[r.Input.Candidates[0].EquipmentId],TimeSpan.FromSeconds(30),default);
    private async Task Expire(Guid id)
    {
        // Test simulates loss of a process at a durable boundary without waiting for wall-clock lease expiry.
        await using var cmd=database.Source.CreateCommand("UPDATE operations.reasoning_jobs SET lease_until=CASE WHEN status=2 THEN clock_timestamp()-interval '1 second' ELSE NULL END,available_at=clock_timestamp()-interval '1 second' WHERE id=@id");cmd.Parameters.AddWithValue("id",id);await cmd.ExecuteNonQueryAsync();
    }
    private sealed class PausedProvider:IndustrialCopilot.Application.Abstractions.AI.ILlmProvider
    {
        public readonly TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<IndustrialCopilot.Application.Abstractions.AI.Models.ToolCompletionResponse> CompleteWithToolsAsync(IndustrialCopilot.Application.Abstractions.AI.Models.CompletionRequest r,IReadOnlyList<IndustrialCopilot.Application.Abstractions.AI.Models.ToolDefinition> t,CancellationToken ct)
        {Entered.TrySetResult();await Task.Delay(Timeout.Infinite,ct);throw new InvalidOperationException();}
        public Task<IndustrialCopilot.Application.Abstractions.AI.Models.CompletionResponse> CompleteAsync(IndustrialCopilot.Application.Abstractions.AI.Models.CompletionRequest r,CancellationToken ct)=>throw new NotSupportedException();
        public IAsyncEnumerable<IndustrialCopilot.Application.Abstractions.AI.Models.StreamingChunk> StreamAsync(IndustrialCopilot.Application.Abstractions.AI.Models.CompletionRequest r,CancellationToken ct)=>throw new NotSupportedException();
        public Task<IndustrialCopilot.Application.Abstractions.AI.Models.EmbeddingResult> GenerateEmbeddingsAsync(IndustrialCopilot.Application.Abstractions.AI.Models.EmbeddingRequest r,CancellationToken ct)=>throw new NotSupportedException();
    }
    [PostgresFact]
    public async Task HostedWorkerClaimsOnlyCapacityAndGracefulStopReturnsUnfinishedWorkWithoutCancellingBusinessRun()
    {
        var r=await Request();var second=await Request();var j=await Jobs.SubmitAsync(new("actor",r.RunId.ToString(),r),default);
        var other=await Jobs.SubmitAsync(new("actor",second.RunId.ToString(),second),default);
        var scope=new ReasoningJobExecutionScope((_,_)=>true);var workflows=new PostgresWorkflowStore(database.Source,scope);
        var provider=new PausedProvider();var candidate=r.Input.Candidates[0];
        var knowledge=new PostgresKnowledgeStore(database.Source,new(new EmbeddingSpace("paused","unused",2),"binding"),provider);
        var policy=new ExactProcedureSafetyPolicy([new(candidate,["Check"],["Inspect"],"Scope",[new(Guid.NewGuid(),"isolate",true)])]);
        var orchestrator=new MaintenanceOrchestrator(provider,knowledge,workflows,policy,new PostgresRunTraceStore(database.Source,new TestAccessPolicy(),scope));
        using var worker=new IndustrialCopilot.Worker.ReasoningWorker(Jobs,new(Jobs,orchestrator,scope),
            new([candidate.EquipmentId,second.Input.Candidates[0].EquipmentId],concurrency:1),Microsoft.Extensions.Logging.Abstractions.NullLogger<IndustrialCopilot.Worker.ReasoningWorker>.Instance);
        await worker.StartAsync(default);await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        Assert.Empty((await Jobs.GetAsync(other.JobId,default))!.Attempts);
        using var stop=new CancellationTokenSource(TimeSpan.FromSeconds(15));await worker.StopAsync(stop.Token);
        var released=(await Jobs.GetAsync(j.JobId,default))!;
        Assert.Equal(ReasoningJobStatus.Queued,released.Status);Assert.Equal("worker_interrupted",released.FailureCode);
        Assert.False((await workflows.GetRunAsync(r.RunId,default))!.Run.IsCancellationRequested);
        Assert.Empty((await Jobs.GetAsync(other.JobId,default))!.Attempts);
    }
    [PostgresFact]
    public async Task ConcurrentEquivalentSubmissionsConvergeAndDifferentPayloadConflicts()
    {
        var r=await Request();var key=Guid.NewGuid().ToString();
        var all=await Task.WhenAll(Enumerable.Range(0,12).Select(_=>Jobs.SubmitAsync(new("actor",key,r),default)));
        Assert.Single(all.Select(j=>j.JobId).Distinct());Assert.All(all,j=>Assert.Equal(ReasoningJobStatus.Queued,j.Status));
        var freshIds=new MaintenanceReasoningRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),r.Input);
        Assert.Equal(all[0].JobId,(await Jobs.SubmitAsync(new("actor",key,freshIds),default)).JobId);
        var changed=new MaintenanceReasoningRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("other symptom",r.Input.Candidates,[]));
        await Assert.ThrowsAsync<JobSubmissionConflictException>(()=>Jobs.SubmitAsync(new("actor",key,changed),default));
        Assert.Equal(MaintenanceRunStatus.Queued,(await new PostgresWorkflowStore(database.Source).GetRunAsync(r.RunId,default))!.Run.Status);
    }
    [PostgresFact]
    public async Task OneValidOwnerExpiredOwnerFencedAndRecoveryGetsSeparateExecution()
    {
        var r=await Request("ar-EG");var j=await Jobs.SubmitAsync(new("actor","key",r),default);
        var claims=await Task.WhenAll(Enumerable.Range(0,10).Select(_=>Claim(r)));var old=Assert.Single(claims,c=>c is not null)!;
        Assert.Equal("ar-EG",old.Request.ResponseCulture);
        await Expire(j.JobId);var fresh=(await Claim(r))!;
        Assert.NotEqual(old.LeaseToken,fresh.LeaseToken);Assert.NotEqual(old.Request.ExecutionId,fresh.Request.ExecutionId);
        Assert.Equal(2,fresh.Attempt);Assert.Equal(JobLeaseState.Lost,await Jobs.RenewAsync(old,TimeSpan.FromSeconds(30),default));
        var scope=new ReasoningJobExecutionScope((_,_)=>true);var workflows=new PostgresWorkflowStore(database.Source,scope);
        var run=(await workflows.GetRunAsync(r.RunId,default))!;run.Run.Start();
        using(scope.Enter(old))await Assert.ThrowsAsync<JobLeaseLostException>(()=>workflows.TrySaveRunAsync(run.Run,run.ConcurrencyToken,default));
        using(scope.Enter(fresh))Assert.NotNull(await workflows.TrySaveRunAsync(run.Run,run.ConcurrencyToken,default));
        await Jobs.ReportAsync(fresh,new(MaintenanceProgressKind.AgentStarted,r.RunId,IndustrialCopilot.Application.Abstractions.Agents.AgentRole.SymptomMatcher),default);
        Assert.Single(await Jobs.ReadProgressAsync(j.JobId,0,64,default));
        Assert.Equal(ReasoningJobPhase.MatchingSymptoms,(await Jobs.GetAsync(j.JobId,default))!.Phase);
        await Assert.ThrowsAsync<JobLeaseLostException>(()=>Jobs.ReportAsync(old,new(MaintenanceProgressKind.Failed,r.RunId),default));
    }
    [PostgresFact]
    public async Task CancellationPersistsAcrossStoreRecreationAndPreventsQueuedAndRecoveredExecution()
    {
        foreach(var running in new[]{false,true})
        {
            var r=await Request();var j=await Jobs.SubmitAsync(new("actor",r.RunId.ToString(),r),default);
            var claim=running?await Claim(r):null;
            var cancelled=(await Jobs.RequestCancellationAsync(j.JobId,default))!;Assert.True(cancelled.CancellationRequested);
            if(claim is not null)
            {
                Assert.Equal(ReasoningJobStatus.Running,cancelled.Status);
                Assert.Equal(JobLeaseState.CancellationRequested,await Jobs.RenewAsync(claim,TimeSpan.FromSeconds(30),default));
                await Expire(j.JobId);
            }
            Assert.Null(await Claim(r));
            Assert.Equal(ReasoningJobStatus.Cancelled,(await Jobs.GetAsync(j.JobId,default))!.Status);
            Assert.Equal(MaintenanceRunStatus.Cancelled,(await new PostgresWorkflowStore(database.Source).GetRunAsync(r.RunId,default))!.Run.Status);
        }
    }
    [PostgresFact]
    public async Task RecoveryAttemptsAreBoundedAndGracefulReleaseHasBackoff()
    {
        var r=await Request();var j=await Jobs.SubmitAsync(new("actor","bounded",r),default);
        var first=(await Claim(r))!;await Jobs.ReleaseAsync(first,default);Assert.Null(await Claim(r));
        await Expire(j.JobId);
        for(var number=2;number<=3;number++)
        {var claim=(await Claim(r))!;Assert.Equal(number,claim.Attempt);await Expire(j.JobId);}
        Assert.Null(await Claim(r));
        var failed=(await Jobs.GetAsync(j.JobId,default))!;Assert.Equal(ReasoningJobStatus.Failed,failed.Status);Assert.Equal("recovery_exhausted",failed.FailureCode);
    }
    [PostgresFact]
    public async Task ReplayedPipelinePublishesOnceThenRecoveryRecognizesPublishedReviewWithoutApprovalOrDispatch()
    {
        foreach(var culture in new[]{"en-US","ar-EG"})
        {
            var r=await Request(culture);var j=await Jobs.SubmitAsync(new("actor",r.RunId.ToString(),r),default);var first=(await Claim(r))!;
            var scope=new ReasoningJobExecutionScope((_,_)=>true);var workflows=new PostgresWorkflowStore(database.Source,scope);
            // Crash after starting the business run. The next attempt replays the pipeline from this safe boundary.
            var run=(await workflows.GetRunAsync(r.RunId,default))!;run.Run.Start();
            using(scope.Enter(first))await workflows.TrySaveRunAsync(run.Run,run.ConcurrencyToken,default);
            await Expire(j.JobId);var claim=(await Claim(r))!;
            var provider=new PipelinePersistenceTests.ScriptedProvider();var c=r.Input.Candidates[0];
            var knowledge=new PostgresKnowledgeStore(database.Source,new(new EmbeddingSpace("job-v1","test-model",2),"test-binding"),provider);
            await knowledge.ReplaceRevisionAsync(new(c.DocumentId,c.ManualRevisionId,"job-v1",[new(new(c.DocumentId,c.ManualRevisionId,Guid.NewGuid(),"page 7","Vibration: Check vibration. Inspect seal."),new float[]{1,0})]),default);
            var requirement=Guid.NewGuid();var policy=new ExactProcedureSafetyPolicy([new(c,["Check vibration"],["Inspect seal"],"Inspect isolated pump",[new(requirement,"Isolate energy",true)])]);
            var traces=new PostgresRunTraceStore(database.Source,new TestAccessPolicy(),scope);
            var orchestrator=new MaintenanceOrchestrator(provider,knowledge,workflows,policy,traces);
            using(scope.Enter(claim))
                Assert.Equal(MaintenanceReasoningOutcome.Proposed,(await orchestrator.ExecuteAsync(claim.Request,default,durableAttempt:true,durableProgress:(p,ct)=>Jobs.ReportAsync(claim,p,ct))).Outcome);
            // Crash after atomic publication, before job completion acknowledgement.
            await Expire(j.JobId);Assert.Null(await Claim(r));
            var done=(await Jobs.GetAsync(j.JobId,default))!;Assert.Equal(ReasoningJobStatus.Succeeded,done.Status);Assert.Equal(2,done.Attempts.Count);
            var order=(await workflows.GetWorkOrderAsync(r.WorkOrderId,default))!.Order;
            Assert.Equal(WorkOrderStatus.PendingApproval,order.Status);Assert.Empty(order.ApprovalHistory);
            Assert.Equal(requirement,Assert.Single(order.SafetyPrerequisites).Id);Assert.Null(order.SafetyPrerequisites[0].Verification);
            Assert.Throws<InvalidOperationException>(()=>order.Dispatch(order.Revision));
            Assert.Equal(MaintenanceRunStatus.WaitingForApproval,(await workflows.GetRunAsync(r.RunId,default))!.Run.Status);
            await using var count=database.Source.CreateCommand("SELECT count(*) FROM operations.work_orders WHERE run_id=@id");count.Parameters.AddWithValue("id",r.RunId);Assert.Equal(1L,await count.ExecuteScalarAsync());
            await using var dispatch=database.Source.CreateCommand("SELECT count(*) FROM operations.dispatch_attempts WHERE run_id=@id");dispatch.Parameters.AddWithValue("id",r.RunId);Assert.Equal(0L,await dispatch.ExecuteScalarAsync());
            await Assert.ThrowsAsync<JobCancellationConflictException>(()=>Jobs.RequestCancellationAsync(j.JobId,default));
            Assert.NotNull(await traces.GetAsync(claim.Request.ExecutionId,default));
        }
    }
}
