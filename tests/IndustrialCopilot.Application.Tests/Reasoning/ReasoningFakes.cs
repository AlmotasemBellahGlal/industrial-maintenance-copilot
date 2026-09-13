using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Abstractions.Tracing;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;

namespace IndustrialCopilot.Application.Tests.Reasoning;

internal sealed class ScriptedLlm : ILlmProvider
{
    internal readonly Queue<Func<CompletionRequest,CancellationToken,Task<ToolCompletionResponse>>> Script=[];
    internal int Calls;
    internal void Output(string content) => Script.Enqueue((_,_)=>Task.FromResult(new ToolCompletionResponse(content,[],new(2,3,5))));
    internal void Tool(string name="retrieve_evidence",string arguments="""{"candidate":0,"query":"vibration"}""",string id="call-1")
    {
        using var json=JsonDocument.Parse(arguments);
        var call=new ToolCall(id,name,json.RootElement);
        Script.Enqueue((_,_)=>Task.FromResult(new ToolCompletionResponse("",[call],new(1,0,1))));
    }
    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,IReadOnlyList<ToolDefinition> tools,CancellationToken token)
    { token.ThrowIfCancellationRequested(); Calls++; return Script.Dequeue()(request,token); }
    public Task<CompletionResponse> CompleteAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
    public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request,CancellationToken token)=>throw new NotSupportedException();
}
internal sealed class FakeRetrieval(EquipmentManualCandidate candidate) : IRetrievalService
{
    internal int Calls; internal bool Fail; internal bool WrongRevision;
    internal readonly Guid Chunk=Guid.NewGuid();
    internal const string Source="SOURCE_PRIVATE: Check vibration. Isolate energy and verify zero energy. Inspect seal.";
    public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery query,RetrievalMode mode,CancellationToken token)
    {
        token.ThrowIfCancellationRequested(); Calls++;
        if(Fail) throw new InvalidOperationException("SECRET retrieval error");
        Assert.Equal(candidate.DocumentId,query.DocumentId); Assert.Equal(candidate.ManualRevisionId,query.ManualRevisionId); Assert.Equal(RetrievalMode.Hybrid,mode);
        return Task.FromResult<IReadOnlyList<RetrievalResult>>([new(candidate.DocumentId,WrongRevision?Guid.NewGuid():candidate.ManualRevisionId,Chunk,"page 7",Source,.4)]);
    }
}
internal sealed class MemoryWorkflow : IWorkflowStore
{
    private readonly Dictionary<Guid,StoredMaintenanceRun> runs=[];
    private readonly Dictionary<Guid,StoredWorkOrder> orders=[];
    private readonly Dictionary<Guid,WorkOrderProposal> proposals=[];
    internal int Publishes; internal bool PublishConflict;
    private static MaintenanceRun Copy(MaintenanceRun r)=>MaintenanceRun.Restore(r.Id,r.EquipmentId,r.ReportedSymptom,r.Status,r.IsCancellationRequested);
    private static WorkOrder Copy(WorkOrder o)=>WorkOrder.Restore(o.Id,o.Content,o.Revision,o.Status,o.SafetyAssessmentRevision,o.SafetyPrerequisites,o.ApprovalHistory);
    public Task<StoredMaintenanceRun?> GetRunAsync(Guid id,CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(runs.TryGetValue(id,out var r)?new StoredMaintenanceRun(Copy(r.Run),r.ConcurrencyToken):null); }
    public Task<string?> TrySaveRunAsync(MaintenanceRun run,string? expected,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(runs.GetValueOrDefault(run.Id)?.ConcurrencyToken!=expected) return Task.FromResult<string?>(null);
        var next=Guid.NewGuid().ToString(); runs[run.Id]=new(Copy(run),next); return Task.FromResult<string?>(next);
    }
    public Task<StoredWorkOrder?> GetWorkOrderAsync(Guid id,CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(orders.TryGetValue(id,out var o)?new StoredWorkOrder(Copy(o.Order),o.ConcurrencyToken,o.MaintenanceRunId):null); }
    public Task<string?> TrySaveWorkOrderAsync(WorkOrder order,Guid? run,string? expected,CancellationToken token)=>throw new NotSupportedException();
    public Task<bool> TryPublishReviewAsync(WorkOrder order,MaintenanceRun run,string expected,WorkOrderProposal proposal,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(PublishConflict || runs[run.Id].ConcurrencyToken!=expected || runs[run.Id].Run.IsCancellationRequested || orders.ContainsKey(order.Id)) return Task.FromResult(false);
        orders[order.Id]=new(Copy(order),Guid.NewGuid().ToString(),run.Id); runs[run.Id]=new(Copy(run),Guid.NewGuid().ToString());
        proposals[order.Id]=proposal; Publishes++; return Task.FromResult(true);
    }
    public Task<WorkOrderProposal?> GetProposalAsync(Guid id,CancellationToken token)=>Task.FromResult(proposals.GetValueOrDefault(id));
}
internal sealed class MemoryTrace : IRunTraceStore
{
    internal RunTraceSnapshot? Current;
    internal bool FailFinal;
    public Task<RunTraceSnapshot?> GetAsync(Guid id,CancellationToken token)
    { token.ThrowIfCancellationRequested(); return Task.FromResult(Current?.ExecutionId==id?Current:null); }
    public Task<bool> TrySaveAsync(RunTraceSnapshot snapshot,long expected,CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if(FailFinal && snapshot.Root.Status==TraceStepStatus.Completed) throw new IOException("Trace unavailable");
        if((Current?.Version??0)!=expected) return Task.FromResult(false);
        Current=snapshot; return Task.FromResult(true);
    }
}
internal sealed class Scenario
{
    internal const string Match="""{"outcome":"Success","candidate":0,"symptoms":[{"description":"vibration","evidence":["e0"]}]}""";
    internal const string Plan="""{"outcome":"Success","steps":[{"order":1,"instruction":"Check vibration","evidence":["e0"]}],"prerequisites":[]}""";
    internal const string Generated="""{"outcome":"Success","description":"Inspect isolated pump","actions":[{"order":1,"instruction":"Inspect seal","evidence":["e0"]}]}""";
    internal EquipmentManualCandidate Candidate=new(Guid.NewGuid(),"pump",Guid.NewGuid(),Guid.NewGuid());
    internal ScriptedLlm Llm=new();
    internal MemoryWorkflow Store=new();
    internal MemoryTrace Trace=new();
    internal FakeRetrieval Retrieval;
    internal MaintenanceReasoningRequest Request;
    internal SafetyPrerequisite Requirement=new(Guid.NewGuid(),"Isolate energy and verify zero energy",true);
    internal Scenario()
    {
        Retrieval=new(Candidate);
        Request=new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("vibration",[Candidate],[]));
    }
    internal ExactProcedureSafetyPolicy Policy()=>new([new(Candidate,["Check vibration"],["Inspect seal"],"Inspect isolated pump",[Requirement])]);
    internal MaintenanceOrchestrator Orchestrator(ReasoningLimits? limits=null)=>new(Llm,Retrieval,Store,Policy(),Trace,limits);
    internal void Success() { Llm.Tool(); Llm.Output(Match); Llm.Output(Plan); Llm.Output(Generated); }
}
