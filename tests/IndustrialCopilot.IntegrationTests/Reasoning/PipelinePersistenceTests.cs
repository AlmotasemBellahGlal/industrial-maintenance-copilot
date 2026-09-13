using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Workflow;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Domain.WorkOrders;
using IndustrialCopilot.Domain.WorkOrders.Safety;
using IndustrialCopilot.Domain.MaintenanceRuns;
using IndustrialCopilot.Infrastructure.Knowledge;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
using IndustrialCopilot.IntegrationTests.Operations;

namespace IndustrialCopilot.IntegrationTests.Reasoning;

public class PipelinePersistenceTests(KnowledgeDatabase database) : IClassFixture<KnowledgeDatabase>
{
    private sealed class ScriptedProvider : ILlmProvider
    {
        private int call;
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,IReadOnlyList<ToolDefinition> tools,CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if(call++==0)
            {
                using var json=JsonDocument.Parse("""{"candidate":0,"query":"vibration"}""");
                return Task.FromResult(new ToolCompletionResponse("",[new("retrieve-1","retrieve_evidence",json.RootElement)],new(1,0,1)));
            }
            var text=call switch {
                2=>"""{"outcome":"Success","candidate":0,"symptoms":[{"description":"vibration","evidence":["e0"]}]}""",
                3=>"""{"outcome":"Success","steps":[{"order":1,"instruction":"Check vibration","evidence":["e0"]}],"prerequisites":[]}""",
                _=>"""{"outcome":"Success","description":"Inspect isolated pump","actions":[{"order":1,"instruction":"Inspect seal","evidence":["e0"]}]}"""
            };
            return Task.FromResult(new ToolCompletionResponse(text,[],new(1,1,2)));
        }
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request,CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(new EmbeddingResult(request.Inputs.Select(_=>(IReadOnlyList<float>)new float[]{1,0}).ToArray(),"test-model")); }
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
        public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
    }
    private static EquipmentManualCandidate Candidate()=>new(Guid.NewGuid(),"pump",Guid.NewGuid(),Guid.NewGuid());

    [PostgresFact]
    public async Task FullPipelineUsesRealRetrievalWorkflowAndTracingPortsAndPreservesGrounding()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var candidate=Candidate(); var provider=new ScriptedProvider();
        var knowledge=new PostgresKnowledgeStore(database.Source,new(new EmbeddingSpace("pipeline-v1","test-model",2),"test-binding"),provider);
        var chunk=Guid.NewGuid();
        await knowledge.ReplaceRevisionAsync(new(candidate.DocumentId,candidate.ManualRevisionId,"pipeline-v1",
            [new(new(candidate.DocumentId,candidate.ManualRevisionId,chunk,"page 7","Vibration: Check vibration. Inspect seal."),new float[]{1,0})]),default);
        IWorkflowStore workflows=new PostgresWorkflowStore(database.Source);
        var traces=new PostgresRunTraceStore(database.Source,new TestAccessPolicy());
        var requirement=new SafetyPrerequisite(Guid.NewGuid(),"Isolate energy and verify zero energy",true);
        var policy=new ExactProcedureSafetyPolicy([new(candidate,["Check vibration"],["Inspect seal"],"Inspect isolated pump",[requirement])]);
        var orchestrator=new MaintenanceOrchestrator(provider,knowledge,workflows,policy,traces);
        var request=new MaintenanceReasoningRequest(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),new("vibration",[candidate],[]));
        var result=await orchestrator.ExecuteAsync(request,default);
        Assert.Equal(MaintenanceReasoningOutcome.Proposed,result.Outcome);
        var order=(await workflows.GetWorkOrderAsync(request.WorkOrderId,default))!.Order;
        Assert.Equal(WorkOrderStatus.PendingApproval,order.Status); Assert.Empty(order.ApprovalHistory);
        Assert.Equal(requirement.Id,Assert.Single(order.SafetyPrerequisites).Id);
        Assert.Null(order.SafetyPrerequisites[0].Verification);
        Assert.Equal(MaintenanceRunStatus.WaitingForApproval,(await workflows.GetRunAsync(request.RunId,default))!.Run.Status);
        var proposal=(await workflows.GetProposalAsync(order.Id,default))!;
        Assert.Equal(chunk,proposal.Actions[0].Evidence[0].ChunkId); Assert.Equal("page 7",proposal.Actions[0].Evidence[0].Locator);
        var trace=(await traces.GetAsync(request.ExecutionId,default))!;
        Assert.Equal(request.CorrelationId,trace.CorrelationId); Assert.Equal(7,trace.Usage.KnownTokenUsage.TotalTokens);
        Assert.Equal(2,trace.Steps.Count(s=>s.OperationName=="deterministic_safety_policy"));
        Assert.Throws<InvalidOperationException>(()=>order.Dispatch(order.Revision));
        Assert.Equal(MaintenanceReasoningOutcome.Conflict,(await orchestrator.ExecuteAsync(request,default)).Outcome);
    }

    private async Task<(IWorkflowStore Store,MaintenanceRun Run,string Token,WorkOrder Order,WorkOrderProposal Proposal)> Publication()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var candidate=Candidate(); IWorkflowStore store=new PostgresWorkflowStore(database.Source);
        var run=new MaintenanceRun(Guid.NewGuid(),candidate.EquipmentId,"vibration"); run.Start();
        var token=(await store.TrySaveRunAsync(run,null,default))!;
        var evidence=new IndustrialCopilot.Application.Abstractions.Agents.Evidence.GroundedEvidence(candidate.DocumentId,candidate.ManualRevisionId,Guid.NewGuid(),"page","source");
        var proposal=new WorkOrderProposal(candidate,"vibration","Inspect isolated pump",[new(1,"Inspect seal",[evidence])],[]);
        var order=new WorkOrder(Guid.NewGuid(),new(candidate.EquipmentId,candidate.DocumentId,candidate.ManualRevisionId,"vibration",proposal.Description,[new(1,"Inspect seal")]));
        order.AssessSafety(1,[new(Guid.NewGuid(),"isolate",true)]); order.SubmitForApproval(2); run.WaitForApproval();
        return(store,run,token,order,proposal);
    }

    [PostgresFact]
    public async Task SimultaneousPublicationsHaveOneWinnerAndStaleRetryCannotCreateAnotherOrder()
    {
        var p=await Publication();
        var results=await Task.WhenAll(p.Store.TryPublishReviewAsync(p.Order,p.Run,p.Token,p.Proposal,default),
            p.Store.TryPublishReviewAsync(p.Order,p.Run,p.Token,p.Proposal,default));
        Assert.Single(results,x=>x); Assert.Single(results,x=>!x);
        Assert.NotEqual(p.Token,(await p.Store.GetRunAsync(p.Run.Id,default))!.ConcurrencyToken);
        Assert.NotNull(await p.Store.GetProposalAsync(p.Order.Id,default));
    }

    [PostgresFact]
    public async Task ExistingOrderConflictRollsBackRunAndCancellationIntentPreventsPublication()
    {
        var p=await Publication();
        await p.Store.TrySaveWorkOrderAsync(p.Order,p.Run.Id,null,default);
        Assert.False(await p.Store.TryPublishReviewAsync(p.Order,p.Run,p.Token,p.Proposal,default));
        var current=(await p.Store.GetRunAsync(p.Run.Id,default))!;
        Assert.Equal(p.Token,current.ConcurrencyToken); Assert.Equal(MaintenanceRunStatus.Running,current.Run.Status);
        current.Run.RequestCancellation();
        var token=(await p.Store.TrySaveRunAsync(current.Run,current.ConcurrencyToken,default))!;
        Assert.False(await p.Store.TryPublishReviewAsync(p.Order,p.Run,token,p.Proposal,default));
        Assert.Null(await p.Store.GetProposalAsync(p.Order.Id,default));
    }

    [PostgresFact]
    public async Task ProposalWriteFailureRollsBackBothAggregates()
    {
        var p=await Publication();
        // PostgreSQL JSONB rejects U+0000; it is injected only into this test's observational citation.
        var candidate=p.Proposal.SelectedCandidate;
        var bad=new WorkOrderProposal(candidate,"vibration",p.Proposal.Description,
            [new(1,"Inspect seal",[new(candidate.DocumentId,candidate.ManualRevisionId,Guid.NewGuid(),"page","invalid\0source")])],[]);
        await Assert.ThrowsAsync<OperationalStoreException>(()=>p.Store.TryPublishReviewAsync(p.Order,p.Run,p.Token,bad,default));
        Assert.Equal(p.Token,(await p.Store.GetRunAsync(p.Run.Id,default))!.ConcurrencyToken);
        Assert.Null(await p.Store.GetWorkOrderAsync(p.Order.Id,default));
        Assert.Null(await p.Store.GetProposalAsync(p.Order.Id,default));
    }
}
