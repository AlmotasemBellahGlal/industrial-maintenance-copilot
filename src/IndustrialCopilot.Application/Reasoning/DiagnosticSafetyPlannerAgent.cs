using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.DiagnosticPlanning;
using IndustrialCopilot.Application.Abstractions.AI;

namespace IndustrialCopilot.Application.Reasoning;

public sealed class DiagnosticSafetyPlannerAgent : IDiagnosticSafetyPlanner
{
    public AgentRole Role => AgentRole.DiagnosticSafetyPlanner;
    private readonly AgentRuntime runtime;
    public DiagnosticSafetyPlannerAgent(ILlmProvider llm,ReasoningLimits? limits=null) : this(new AgentRuntime(llm,limits??new())) { }
    internal DiagnosticSafetyPlannerAgent(AgentRuntime runtime) { this.runtime=runtime; }
    public async Task<DiagnosticPlanResult> PlanAsync(DiagnosticPlanInput input,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        var catalog=new EvidenceCatalog();
        foreach(var item in input.Match.MatchedSymptoms.SelectMany(s=>s.Evidence)) catalog.Add(item);
        try
        {
            var text=await runtime.Complete(Role,"""
                You are the Diagnostic & Safety Planner. Propose ordered diagnostic instructions and advisory prerequisites from supplied evidence.
                You have NO tools. Use only supplied evidence references. Never mark prerequisites mandatory, verified, satisfied or authoritative.
                Success JSON: {"outcome":"Success","steps":[{"order":1,"instruction":"instruction","evidence":["e0"]}],"prerequisites":[{"description":"precaution","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Every step and advisory prerequisite needs grounding. An empty advisory list is not a safety assessment.
                """,JsonSerializer.Serialize(new { input.ReportedSymptom, input.Match.SelectedCandidate,
                    Symptoms=input.Match.MatchedSymptoms.Select(s=>s.Description), Evidence=catalog.Describe() }),[],null,cancellationToken);
            using var document=AgentJson.Parse(text); var root=document.RootElement; var outcome=AgentJson.Outcome(root);
            if(outcome!=AgentOutcome.Success)
            {
                AgentJson.Shape(root,"outcome");
                return outcome==AgentOutcome.InsufficientEvidence ? DiagnosticPlanResult.InsufficientEvidence("Grounding is insufficient.") : DiagnosticPlanResult.CannotProceed("Planning cannot proceed.");
            }
            AgentJson.Shape(root,"outcome","steps","prerequisites");
            var steps=AgentJson.Array(root.GetProperty("steps")).Select(s=>{
                AgentJson.Shape(s,"order","instruction","evidence");
                return new ProposedDiagnosticStep(AgentJson.Number(s.GetProperty("order")),AgentJson.Text(s.GetProperty("instruction")),catalog.Resolve(s.GetProperty("evidence")));
            }).ToArray();
            var safety=AgentJson.Array(root.GetProperty("prerequisites")).Select(s=>{
                AgentJson.Shape(s,"description","evidence");
                return new ProposedSafetyPrerequisite(AgentJson.Text(s.GetProperty("description")),catalog.Resolve(s.GetProperty("evidence")));
            }).ToArray();
            return DiagnosticPlanResult.Success(new(input.Match.SelectedCandidate,steps,safety));
        }
        catch(AgentExecutionLimitException) { return DiagnosticPlanResult.CannotProceed("Planning budget exhausted."); }
        catch(Exception e) when(AgentJson.IsInvalid(e)) { return DiagnosticPlanResult.CannotProceed("Invalid planning output."); }
    }
}
