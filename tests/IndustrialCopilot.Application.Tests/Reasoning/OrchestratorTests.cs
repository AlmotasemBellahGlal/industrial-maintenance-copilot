using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Application.Tests.Reasoning;

public class OrchestratorTests
{
    [Fact]
    public async Task CompletePipelinePersistsGroundedUnapprovedScopeAndTraceTreeWithUsage()
    {
        var s=new Scenario(); s.Success();
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome);
        var order=(await s.Store.GetWorkOrderAsync(result.WorkOrderId!.Value,default))!.Order;
        Assert.Equal(WorkOrderStatus.PendingApproval,order.Status); Assert.Empty(order.ApprovalHistory);
        Assert.Equal(s.Requirement.Id,Assert.Single(order.SafetyPrerequisites).Id);
        Assert.Null(order.SafetyPrerequisites[0].Verification);
        Assert.Throws<InvalidOperationException>(()=>order.Dispatch(order.Revision));
        Assert.Equal(MaintenanceRunStatus.WaitingForApproval,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
        var proposal=(await s.Store.GetProposalAsync(order.Id,default))!;
        Assert.Equal(s.Retrieval.Chunk,proposal.Actions[0].Evidence[0].ChunkId);
        Assert.Equal(FakeRetrieval.Source,proposal.Actions[0].Evidence[0].Snippet);
        var trace=s.Trace.Current!;
        Assert.Equal(s.Request.CorrelationId,trace.CorrelationId); Assert.Equal(s.Request.ExecutionId,trace.ExecutionId);
        Assert.Equal(3,trace.Steps.Count(x=>x.Kind==TraceOperationKind.Agent));
        Assert.Equal(4,trace.Steps.Count(x=>x.Kind==TraceOperationKind.Llm));
        Assert.Equal(2,trace.Steps.Count(x=>x.OperationName=="deterministic_safety_policy"));
        Assert.Contains(trace.Steps,x=>x.Kind==TraceOperationKind.Tool && x.OperationName=="retrieve_evidence");
        Assert.Equal(16,trace.Usage.KnownTokenUsage.TotalTokens);
        Assert.Equal(TraceStepStatus.Completed,trace.Root.Status);
        Assert.DoesNotContain("SOURCE_PRIVATE",JsonSerializer.Serialize(trace));
        Assert.Equal(4,s.Llm.Calls);
    }

    [Theory]
    [InlineData(0,"InsufficientEvidence",MaintenanceReasoningOutcome.InsufficientEvidence)]
    [InlineData(0,"CannotProceed",MaintenanceReasoningOutcome.CannotProceed)]
    [InlineData(1,"CannotProceed",MaintenanceReasoningOutcome.CannotProceed)]
    public async Task UpstreamNonSuccessStopsDownstreamAndBlocksRun(int stage,string outcome,MaintenanceReasoningOutcome expected)
    {
        var s=new Scenario();
        if(stage==1) { s.Llm.Tool(); s.Llm.Output(Scenario.Match); }
        s.Llm.Output(JsonSerializer.Serialize(new {outcome}));
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(expected,result.Outcome); Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(stage==0?1:3,s.Llm.Calls);
        Assert.Equal(MaintenanceRunStatus.Blocked,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SafetyPolicyBlocksUnreviewedPlanOrGeneratedScope(bool finalScope)
    {
        var s=new Scenario(); s.Llm.Tool(); s.Llm.Output(Scenario.Match);
        s.Llm.Output(finalScope?Scenario.Plan:Scenario.Plan.Replace("Check vibration","Operate live machinery"));
        if(finalScope) s.Llm.Output(Scenario.Generated.Replace("Inspect seal","Operate live machinery"));
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.CannotProceed,result.Outcome); Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(finalScope?4:3,s.Llm.Calls);
        Assert.Contains(s.Trace.Current!.Steps,x=>x.OperationName=="deterministic_safety_policy" && x.Status==TraceStepStatus.Failed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProviderAndRetrievalFailuresAreDistinctFromInsufficientEvidenceAndNotRetried(bool retrieval)
    {
        var s=new Scenario();
        if(retrieval) { s.Llm.Tool(); s.Retrieval.Fail=true; }
        else s.Llm.Script.Enqueue((_,_)=>throw new InvalidOperationException("SECRET credential error"));
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Failed,result.Outcome); Assert.Equal(1,s.Llm.Calls);
        Assert.Equal(MaintenanceRunStatus.Failed,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
        Assert.DoesNotContain("SECRET",JsonSerializer.Serialize(s.Trace.Current));
    }

    [Fact]
    public async Task CancellationDuringModelCallPersistsCancellationAndPropagatesWithoutRetry()
    {
        var s=new Scenario(); using var cts=new CancellationTokenSource();
        s.Llm.Script.Enqueue(async(_,ct)=> { cts.Cancel(); await Task.Delay(Timeout.Infinite,ct); throw new Exception(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>s.Orchestrator().ExecuteAsync(s.Request,cts.Token));
        var run=(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run;
        Assert.Equal(MaintenanceRunStatus.Cancelled,run.Status); Assert.True(run.IsCancellationRequested);
        Assert.Equal(TraceStepStatus.Cancelled,s.Trace.Current!.Root.Status);
        Assert.Equal(1,s.Llm.Calls); Assert.Equal(0,s.Store.Publishes);
    }

    [Fact]
    public async Task TimeoutBoundsNonCooperativeModelAndLateResponseCannotPublish()
    {
        var s=new Scenario(); var response=new TaskCompletionSource<ToolCompletionResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        s.Llm.Script.Enqueue((_,_)=>response.Task);
        var result=await s.Orchestrator(new(agentTimeout:TimeSpan.FromMilliseconds(50))).ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.TimedOut,result.Outcome);
        response.SetResult(new(Scenario.Match,[],new(0,0,0)));
        Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(MaintenanceRunStatus.Failed,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
        Assert.Equal(TraceStepStatus.Failed,s.Trace.Current!.Root.Status);
    }

    [Fact]
    public async Task DurableCancellationBetweenStagesStopsWithoutBeingReportedAsTimeout()
    {
        var s=new Scenario();
        s.Llm.Script.Enqueue(async(_,_)=>{
            var current=(await s.Store.GetRunAsync(s.Request.RunId,default))!;
            current.Run.RequestCancellation(); await s.Store.TrySaveRunAsync(current.Run,current.ConcurrencyToken,default);
            return new("""{"outcome":"InsufficientEvidence"}""",[],new(0,0,0));
        });
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Cancelled,result.Outcome);
        Assert.Equal(MaintenanceRunStatus.Cancelled,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
        Assert.Equal(1,s.Llm.Calls);
    }

    [Fact]
    public async Task PublicationConflictDoesNotClaimSuccessOrRetryAgents()
    {
        var s=new Scenario(); s.Success(); s.Store.PublishConflict=true;
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Conflict,result.Outcome); Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(4,s.Llm.Calls);
    }

    [Fact]
    public async Task WorkflowIterationBudgetStopsBeforePlanning()
    {
        var s=new Scenario(); s.Llm.Tool();
        var result=await s.Orchestrator(new(modelTurns:1)).ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.CannotProceed,result.Outcome);
        Assert.Equal(1,s.Llm.Calls); Assert.Equal(0,s.Store.Publishes);
    }

    [Fact]
    public async Task ProviderOriginatedCancellationIsFailureRatherThanOurAgentDeadline()
    {
        var s=new Scenario();
        s.Llm.Script.Enqueue((_,_)=>throw new OperationCanceledException("provider internal timeout"));
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Failed,result.Outcome);
        Assert.Equal(1,s.Llm.Calls);
    }

    [Fact]
    public async Task ObservabilityFailureCannotReverseOrHideCommittedProposal()
    {
        var s=new Scenario(); s.Success(); s.Trace.FailFinal=true;
        var result=await s.Orchestrator().ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome); Assert.False(result.TraceComplete);
        Assert.NotNull(await s.Store.GetWorkOrderAsync(result.WorkOrderId!.Value,default));
        Assert.Equal(MaintenanceRunStatus.WaitingForApproval,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
    }

    [Fact]
    public void ApplicationPortsHaveNoInfrastructureOrProviderAssemblyDependencies()
    {
        var assembly=typeof(IndustrialCopilot.Application.Abstractions.Workflow.IWorkflowStore).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(),a=>a.Name!.Contains("Infrastructure") || a.Name.Contains("Npgsql") || a.Name.Contains("OpenAI") || a.Name.Contains("EntityFramework"));
        Assert.Equal(typeof(WorkOrder),typeof(IndustrialCopilot.Application.Abstractions.Workflow.StoredWorkOrder).GetProperty("Order")!.PropertyType);
    }
}
