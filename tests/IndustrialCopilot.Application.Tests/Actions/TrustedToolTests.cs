using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Actions;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Abstractions.Safety;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Actions;

namespace IndustrialCopilot.Application.Tests.Actions;

public class TrustedToolTests
{
    private sealed class Dependencies : IRetrievalService,IEquipmentContextStore,ISafetyPolicy,IActionAuthorization,IRunTraceStore,IDispatchAttemptStore,IExternalDispatch
    {
        public bool Denied; public bool Fail; public int Invocations; public int Reservations; public int Sends;
        public readonly List<RunTraceSnapshot> Traces=[];
        public Task<bool> AuthorizeAsync(string actor,TrustedAction action,Guid id,CancellationToken ct)=>Task.FromResult(!Denied && actor=="authenticated");
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery q,RetrievalMode mode,CancellationToken ct)
        { ct.ThrowIfCancellationRequested(); Invocations++; if(Fail) throw new IOException("SECRET"); return Task.FromResult<IReadOnlyList<RetrievalResult>>([new(q.DocumentId!.Value,q.ManualRevisionId!.Value,Guid.NewGuid(),"page 1","grounded",10)]); }
        public Task<EquipmentContext?> GetAsync(Guid id,CancellationToken ct) {Invocations++; return Task.FromResult<EquipmentContext?>(new(id,Array.AsReadOnly(new[]{new ManualReference(Guid.NewGuid(),Guid.NewGuid())})));}
        public Task<SafetyAssessment> AssessAsync(DiagnosticPlan p,WorkOrderProposal? w,CancellationToken ct) {Invocations++; return Task.FromResult(SafetyAssessment.Blocked());}
        public Task<bool> TrySaveAsync(RunTraceSnapshot s,long expected,CancellationToken ct) {Traces.Add(s); return Task.FromResult(true);}
        Task<RunTraceSnapshot?> IRunTraceStore.GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<RunTraceSnapshot?>(null);
        public Task<DispatchReservation> ReserveAsync(DispatchCommand c,CancellationToken ct) {Reservations++; return Task.FromResult(new DispatchReservation(DispatchGateOutcome.NotDispatchable,null,DispatchGateFailure.Safety));}
        Task<DispatchAttempt?> IDispatchAttemptStore.GetAsync(Guid id,CancellationToken ct)=>Task.FromResult<DispatchAttempt?>(null);
        public Task<DispatchAttemptSession?> OpenAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException();
        public Task<ExternalDispatchResult> SendAsync(DispatchAttempt a,CancellationToken ct) {Sends++; throw new InvalidOperationException();}
        public Task<ExternalDispatchResult> ReconcileAsync(Guid id,CancellationToken ct)=>throw new InvalidOperationException();
    }
    private static TrustedToolExecutor Executor(Dependencies d)=>new(d,d,d,new(d,d,d),d,d);
    private static TrustedToolContext Context(AgentRole? role=null)=>new("authenticated",role,Guid.NewGuid());
    private static ToolCall Call(string name,object args)=>new("call-1",name,JsonSerializer.SerializeToElement(args));
    private static ToolCall Retrieval()=>Call("retrieve_manual_evidence",new {query="noise",topK=5,documentId=Guid.NewGuid(),manualRevisionId=Guid.NewGuid(),mode="Hybrid"});
    [Fact]
    public void RegistryHasFourRealCapabilitiesWithExplicitEffectsAndClosedSchemas()
    {
        Assert.Equal(4,TrustedToolExecutor.Tools.Count);
        Assert.Equal(2,TrustedToolExecutor.Tools.Count(t=>t.Effect==ToolEffect.ReadOnly));
        Assert.Single(TrustedToolExecutor.Tools,t=>t.Effect==ToolEffect.Validation);
        Assert.Equal("dispatch_approved_work_order",Assert.Single(TrustedToolExecutor.Tools,t=>t.Effect==ToolEffect.ExternalSideEffect).Definition.Name);
        Assert.All(TrustedToolExecutor.Tools,t=>Assert.False(t.Definition.ArgumentsSchema.GetProperty("additionalProperties").GetBoolean()));
    }
    [Theory]
    [InlineData(AgentRole.SymptomMatcher)] [InlineData(AgentRole.DiagnosticSafetyPlanner)] [InlineData(AgentRole.WorkOrderGenerator)]
    public async Task NoReasoningRoleCanInvokeDispatchRegardlessOfArguments(AgentRole role)
    {
        var d=new Dependencies(); var result=await Executor(d).ExecuteAsync(Call("dispatch_approved_work_order",new {workOrderId=Guid.NewGuid(),revision=2,concurrencyToken="token"}),Context(role),default);
        Assert.Equal(ToolExecutionOutcome.Forbidden,result.Outcome); Assert.Equal(0,d.Reservations); Assert.Equal(0,d.Sends);
    }
    [Fact]
    public async Task HostDispatchStillMustPassCurrentPersistedGate()
    {
        var d=new Dependencies(); var result=await Executor(d).ExecuteAsync(Call("dispatch_approved_work_order",new {workOrderId=Guid.NewGuid(),revision=2,concurrencyToken="token"}),Context(),default);
        Assert.Equal(DispatchGateOutcome.NotDispatchable,result.Dispatch!.Outcome); Assert.Equal(1,d.Reservations); Assert.Equal(0,d.Sends);
        var names=Assert.Single(d.Traces).Steps.Select(s=>s.OperationName);
        Assert.Contains("authorize_Dispatch_granted",names);
        Assert.Contains("dispatch_gate_NotDispatchable",names);
        Assert.Contains("dispatch_gate_failure_Safety",names);
    }
    [Fact]
    public async Task MissingContextActorOnlyAndAuthorizationDenialCannotRead()
    {
        var d=new Dependencies(); var e=Executor(d);
        Assert.Equal(ToolExecutionOutcome.Forbidden,(await e.ExecuteAsync(Retrieval(),null,default)).Outcome);
        Assert.Equal(ToolExecutionOutcome.Forbidden,(await e.ExecuteAsync(Retrieval(),Context() with{ActorId="claimed-supervisor"},default)).Outcome);
        d.Denied=true; Assert.Equal(ToolExecutionOutcome.Forbidden,(await e.ExecuteAsync(Retrieval(),Context(),default)).Outcome); Assert.Equal(0,d.Invocations);
    }
    [Theory]
    [InlineData("{\"equipmentId\":\"bad\"}")]
    [InlineData("{\"equipmentId\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("{\"equipmentId\":7}")]
    [InlineData("{\"equipmentId\":\"bad\",\"equipmentId\":\"bad\"}")]
    [InlineData("{\"equipmentId\":\"bad\",\"approved\":true}")]
    public async Task MalformedAndExtraOrDuplicateArgumentsCannotInvoke(string json)
    {
        using var document=JsonDocument.Parse(json); var d=new Dependencies();
        Assert.Equal(ToolExecutionOutcome.InvalidArguments,(await Executor(d).ExecuteAsync(new("id","get_equipment_context",document.RootElement),Context(),default)).Outcome);
        Assert.Equal(0,d.Invocations);
    }
    [Fact]
    public async Task UnknownToolAndProviderFailureProduceSafeTracesAndResults()
    {
        var d=new Dependencies {Fail=true}; var e=Executor(d);
        Assert.Equal(ToolExecutionOutcome.UnknownTool,(await e.ExecuteAsync(Call("SECRET",new{}),Context(),default)).Outcome);
        var result=await e.ExecuteAsync(Retrieval(),Context(),default);
        Assert.Equal(ToolExecutionOutcome.Unavailable,result.Outcome);
        Assert.DoesNotContain("SECRET",JsonSerializer.Serialize(d.Traces));
        Assert.DoesNotContain("SECRET",JsonSerializer.Serialize(result));
    }
    [Fact]
    public async Task CancellationBeforeExecutionDoesNotInvokeCapabilities()
    {
        var d=new Dependencies(); using var c=new CancellationTokenSource(); c.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Executor(d).ExecuteAsync(Retrieval(),Context(),c.Token)); Assert.Equal(0,d.Invocations);
    }
    [Fact]
    public async Task ReadsAreGroundedAndSafetyUsesTrustedContextNotModelSuppliedApproval()
    {
        var d=new Dependencies(); var e=Executor(d);
        Assert.Single((await e.ExecuteAsync(Retrieval(),Context(AgentRole.SymptomMatcher),default)).Evidence!);
        Assert.Single((await e.ExecuteAsync(Call("get_equipment_context",new{equipmentId=Guid.NewGuid()}),Context(),default)).Equipment!.Manuals);
        var candidate=new EquipmentManualCandidate(Guid.NewGuid(),"pump",Guid.NewGuid(),Guid.NewGuid());
        var evidence=new GroundedEvidence(candidate.DocumentId,candidate.ManualRevisionId,Guid.NewGuid(),"page 1","isolate");
        var plan=new DiagnosticPlan(candidate,[new(1,"isolate",[evidence])],[]);
        var context=Context(AgentRole.DiagnosticSafetyPlanner) with{Plan=plan};
        var result=await e.ExecuteAsync(Call("validate_safety_requirements",new{}),context,default);
        Assert.False(result.Assessment!.CanProceed); Assert.Null(result.Dispatch);
        Assert.Equal(ToolExecutionOutcome.InvalidArguments,(await e.ExecuteAsync(Call("validate_safety_requirements",new{safe=true}),context,default)).Outcome);
        Assert.Equal(ToolExecutionOutcome.InvalidArguments,(await e.ExecuteAsync(Call("validate_safety_requirements",new{}),Context(),default)).Outcome);
    }
}
