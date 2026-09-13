using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.WorkOrders;
using IndustrialCopilot.Application.Abstractions.AI;

namespace IndustrialCopilot.Application.Reasoning;

public sealed class WorkOrderGeneratorAgent : IWorkOrderGenerator
{
    public AgentRole Role => AgentRole.WorkOrderGenerator;
    private readonly AgentRuntime runtime;
    public WorkOrderGeneratorAgent(ILlmProvider llm,ReasoningLimits? limits=null) : this(new AgentRuntime(llm,limits??new())) { }
    internal WorkOrderGeneratorAgent(AgentRuntime runtime) { this.runtime=runtime; }
    public async Task<WorkOrderGenerationResult> GenerateAsync(WorkOrderGenerationInput input,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        var catalog=new EvidenceCatalog();
        foreach(var item in input.Plan.Steps.SelectMany(s=>s.Evidence).Concat(input.Plan.SafetyPrerequisites.SelectMany(s=>s.Evidence))) catalog.Add(item);
        try
        {
            var text=await runtime.Complete(Role,"""
                You are the Work Order Generator. Propose a description and ordered executable actions based on the reviewed diagnostic plan.
                You have NO tools. Use only supplied evidence references. Do not expand the planned maintenance scope.
                Success JSON: {"outcome":"Success","description":"proposal","actions":[{"order":1,"instruction":"action","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Safety proposals are preserved by trusted code. Never generate safety flags, identities, approval or dispatch fields.
                """,JsonSerializer.Serialize(new { input.ReportedSymptom, input.Plan.SelectedCandidate,
                    Steps=input.Plan.Steps.Select(s=>new { s.Order,s.Instruction }),
                    Prerequisites=input.Plan.SafetyPrerequisites.Select(s=>s.Description), Evidence=catalog.Describe() }),[],null,cancellationToken);
            using var document=AgentJson.Parse(text); var root=document.RootElement; var outcome=AgentJson.Outcome(root);
            if(outcome!=AgentOutcome.Success)
            {
                AgentJson.Shape(root,"outcome");
                return outcome==AgentOutcome.InsufficientEvidence ? WorkOrderGenerationResult.InsufficientEvidence("Grounding is insufficient.") : WorkOrderGenerationResult.CannotProceed("Generation cannot proceed.");
            }
            AgentJson.Shape(root,"outcome","description","actions");
            var actions=AgentJson.Array(root.GetProperty("actions")).Select(s=>{
                AgentJson.Shape(s,"order","instruction","evidence");
                return new ProposedWorkOrderAction(AgentJson.Number(s.GetProperty("order")),AgentJson.Text(s.GetProperty("instruction")),catalog.Resolve(s.GetProperty("evidence")));
            }).ToArray();
            return WorkOrderGenerationResult.Success(new(input.Plan.SelectedCandidate,input.ReportedSymptom,AgentJson.Text(root.GetProperty("description")),actions,input.Plan.SafetyPrerequisites));
        }
        catch(AgentExecutionLimitException) { return WorkOrderGenerationResult.CannotProceed("Generation budget exhausted."); }
        catch(Exception e) when(AgentJson.IsInvalid(e)) { return WorkOrderGenerationResult.CannotProceed("Invalid generation output."); }
    }
}
