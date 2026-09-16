using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Ask;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Application.Tests.Ask;
namespace IndustrialCopilot.Application.Tests.Reasoning;

public class ResilienceTests
{
    private sealed class Fallback : IPlainRagFallback
    {
        public int Calls;public bool Wait;
        public async Task<GroundedFallbackResult> AnswerAsync(EquipmentManualCandidate c,string question,string culture,CancellationToken ct)
        {Calls++;if(Wait)await Task.Delay(Timeout.Infinite,ct);return new(true,"advisory",[]);}
    }
    private static MaintenanceOrchestrator Pipeline(Scenario s,IPlainRagFallback fallback,ReasoningLimits? limits=null,ISafetyPolicy? policy=null)=>
        new(s.Llm,s.Retrieval,s.Store,policy??s.Policy(),s.Trace,limits??new(retryDelay:TimeSpan.Zero),fallback:fallback);
    [Fact]public async Task TransientReadOnlyCallRetriesOnceThenContinuesWithDistinctTraceAttempts()
    {
        var s=new Scenario();s.Llm.Script.Enqueue((_,_)=>throw new DependencyFailureException(DependencyFailureKind.Transient));s.Success();
        var fallback=new Fallback();var events=new List<MaintenanceProgress>();
        var result=await Pipeline(s,fallback).ExecuteAsync(s.Request,default,durableProgress:(p,_)=>{events.Add(p);return Task.CompletedTask;});
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome);Assert.Equal(5,s.Llm.Calls);Assert.Equal(0,fallback.Calls);
        Assert.Contains(events,e=>e.Kind==MaintenanceProgressKind.RetryScheduled && e.Attempt==2 && e.ReasonCode=="transient_dependency");
        Assert.Equal(5,s.Trace.Current!.Steps.Count(x=>x.Kind==TraceOperationKind.Llm));Assert.Equal(1,s.Store.Publishes);
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task ExhaustedTransientFailureUsesRealGroundedAskOrRefusesWithoutPublication(bool empty)
    {
        var s=new Scenario();for(var n=0;n<2;n++)s.Llm.Script.Enqueue((_,_)=>throw new DependencyFailureException(DependencyFailureKind.Transient));
        var provider=new AskTests.Provider();var retrieval=new AskTests.Retrieval{Empty=empty};
        var result=await Pipeline(s,new PlainRagFallback(new AskService(retrieval,provider))).ExecuteAsync(s.Request,default);
        Assert.Equal(empty?MaintenanceReasoningOutcome.DegradedRefused:MaintenanceReasoningOutcome.Degraded,result.Outcome);
        Assert.Equal("transient_exhausted",result.DegradationReason);Assert.Null(result.WorkOrderId);Assert.Equal(0,s.Store.Publishes);Assert.Equal(2,s.Llm.Calls);
        Assert.Equal(MaintenanceRunStatus.Failed,(await s.Store.GetRunAsync(s.Request.RunId,default))!.Run.Status);
        if(empty){Assert.Null(provider.Request);Assert.Empty(result.Citations!);}
        else {var citation=Assert.Single(result.Citations!);Assert.Equal(s.Candidate.DocumentId,citation.DocumentId);Assert.Equal(s.Candidate.ManualRevisionId,citation.ManualRevisionId);Assert.Equal(retrieval.Snippet,citation.Snippet);}
        Assert.Contains(s.Trace.Current!.Steps,x=>x.OperationName=="grounded_rag_fallback");
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)]public async Task TerminalAuthorizationAndSchemaFailuresNeverTriggerRetryOrFallback(int kind)
    {
        var s=new Scenario();var fallback=new Fallback();
        if(kind==2)s.Llm.Output("not valid json");
        else s.Llm.Script.Enqueue((_,_)=>throw (kind==0?new DependencyFailureException(DependencyFailureKind.Terminal):new UnauthorizedAccessException()));
        var result=await Pipeline(s,fallback).ExecuteAsync(s.Request,default);
        Assert.NotEqual(MaintenanceReasoningOutcome.Degraded,result.Outcome);Assert.Equal(1,s.Llm.Calls);Assert.Equal(0,fallback.Calls);Assert.Equal(0,s.Store.Publishes);
    }
    [Fact]public async Task AgentTimeoutFallsBackOnceAndFallbackTimeoutRemainsBounded()
    {
        var s=new Scenario();s.Llm.Script.Enqueue(async(_,ct)=>{await Task.Delay(Timeout.Infinite,ct);throw new Exception();});
        var fallback=new Fallback{Wait=true};var watch=System.Diagnostics.Stopwatch.StartNew();
        var result=await Pipeline(s,fallback,new(agentTimeout:TimeSpan.FromMilliseconds(50),fallbackTimeout:TimeSpan.FromMilliseconds(50))).ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.TimedOut,result.Outcome);Assert.Equal("agent_timeout",result.DegradationReason);
        Assert.Equal(1,fallback.Calls);Assert.Equal(1,s.Llm.Calls);Assert.Equal(0,s.Store.Publishes);Assert.True(watch.Elapsed<TimeSpan.FromSeconds(3));
    }
    [Fact]public async Task CallerCancellationStopsRetryBackoffWithoutFallback()
    {
        var s=new Scenario();using var stop=new CancellationTokenSource();var fallback=new Fallback();
        s.Llm.Script.Enqueue((_,_)=>throw new DependencyFailureException(DependencyFailureKind.Transient));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Pipeline(s,fallback,new(retryDelay:TimeSpan.FromSeconds(1))).ExecuteAsync(s.Request,stop.Token,
            durableProgress:(p,_)=>{if(p.Kind==MaintenanceProgressKind.RetryScheduled)stop.Cancel();return Task.CompletedTask;}));
        Assert.Equal(1,s.Llm.Calls);Assert.Equal(0,fallback.Calls);Assert.Equal(0,s.Store.Publishes);
    }
    [Fact]public async Task SafetyRejectionIsNotADegradationTrigger()
    {
        var s=new Scenario();s.Llm.Tool();s.Llm.Output(Scenario.Match);s.Llm.Output(Scenario.Plan.Replace("Check vibration","Operate live machinery"));
        var fallback=new Fallback();var result=await Pipeline(s,fallback).ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.CannotProceed,result.Outcome);Assert.Equal(0,fallback.Calls);Assert.Equal(0,s.Store.Publishes);
    }
    private sealed class SlowSafety : ISafetyPolicy
    {public async Task<SafetyAssessment> AssessAsync(DiagnosticPlan p,WorkOrderProposal? o,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);throw new Exception();}}
    [Fact]public async Task SafetyTimeoutCannotBeMistakenForAnEligibleAgentTimeout()
    {
        var s=new Scenario();s.Success();var fallback=new Fallback();
        var result=await Pipeline(s,fallback,new(agentTimeout:TimeSpan.FromSeconds(2)),new SlowSafety()).ExecuteAsync(s.Request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Failed,result.Outcome);Assert.Equal(0,fallback.Calls);Assert.Equal(0,s.Store.Publishes);
    }
    [Fact]public async Task CancellationDuringFallbackStopsGenerationAndCannotPublish()
    {
        var s=new Scenario();s.Llm.Script.Enqueue((_,_)=>throw new DependencyFailureException(DependencyFailureKind.Timeout));
        var provider=new AskTests.Provider{Wait=true};using var stop=new CancellationTokenSource();
        var pending=Pipeline(s,new PlainRagFallback(new AskService(new AskTests.Retrieval(),provider))).ExecuteAsync(s.Request,stop.Token);
        await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);Assert.True(provider.Cancelled);Assert.Equal(0,s.Store.Publishes);
    }
    private sealed class FailedRead(bool forbidden):IRetrievalService
    {
        public int Calls;
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery q,RetrievalMode m,CancellationToken ct)
        {Calls++;throw forbidden?new UnauthorizedAccessException():new DependencyFailureException(DependencyFailureKind.Transient);}
    }
    [Theory][InlineData(false)][InlineData(true)]public async Task ReadOnlyToolExhaustionCanDegradeButAuthorizationCannot(bool forbidden)
    {
        var s=new Scenario();s.Llm.Tool();var read=new FailedRead(forbidden);var fallback=new Fallback();
        var pipeline=new MaintenanceOrchestrator(s.Llm,read,s.Store,s.Policy(),s.Trace,new(retryDelay:TimeSpan.Zero),fallback:fallback);
        var result=await pipeline.ExecuteAsync(s.Request,default);
        Assert.Equal(forbidden?1:2,read.Calls);Assert.Equal(forbidden?0:1,fallback.Calls);Assert.Equal(0,s.Store.Publishes);
        Assert.Equal(forbidden?MaintenanceReasoningOutcome.Failed:MaintenanceReasoningOutcome.Degraded,result.Outcome);
    }
}
