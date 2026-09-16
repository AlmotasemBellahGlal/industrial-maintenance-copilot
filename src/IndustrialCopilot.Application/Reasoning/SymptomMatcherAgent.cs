using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;

namespace IndustrialCopilot.Application.Reasoning;

public sealed class SymptomMatcherAgent : ISymptomMatcher
{
    public AgentRole Role => AgentRole.SymptomMatcher;
    private readonly AgentRuntime runtime;
    private readonly IRetrievalService retrieval;
    private readonly ReasoningLimits limits;
    public SymptomMatcherAgent(ILlmProvider llm,IRetrievalService retrieval,ReasoningLimits? limits=null)
        : this(new AgentRuntime(llm,limits??new()),retrieval,limits??new()) { }
    internal SymptomMatcherAgent(AgentRuntime runtime,IRetrievalService retrieval,ReasoningLimits limits)
    { this.runtime=runtime; this.retrieval=retrieval; this.limits=limits; }
    public async Task<SymptomMatchResult> MatchAsync(SymptomMatchInput input,CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input); cancellationToken.ThrowIfCancellationRequested();
        if(input.Candidates.Count>8 || input.ReportedSymptom.Length>4000) return SymptomMatchResult.CannotProceed("Input budget exceeded.");
        var catalog=new EvidenceCatalog();
        using var schema=JsonDocument.Parse("""{"type":"object","properties":{"candidate":{"type":"integer","minimum":0},"query":{"type":"string"}},"required":["candidate","query"],"additionalProperties":false}""");
        ToolDefinition[] tools=[new("retrieve_evidence","Search a supplied candidate's exact manual revision.",schema.RootElement)];
        try
        {
            var text=await runtime.Complete(Role,"""
                You are the Symptom Matcher. Retrieve manual evidence before matching; use only retrieve_evidence.
                Candidate numbers are zero-based indexes into the supplied candidates. Evidence reference strings are supplied by the tool.
                Success JSON: {"outcome":"Success","candidate":0,"symptoms":[{"description":"matched symptom","evidence":["e0"]}]}.
                Failure JSON: {"outcome":"InsufficientEvidence"} or {"outcome":"CannotProceed"}.
                Match only what retrieved evidence supports. Never invent identifiers, references, locators or snippets.
                """,JsonSerializer.Serialize(new { input.ReportedSymptom,input.Candidates }),tools,async(call,ct)=>
            {
                AgentJson.Shape(call.Arguments,"candidate","query");
                var index=AgentJson.Number(call.Arguments.GetProperty("candidate"));
                if(index<0 || index>=input.Candidates.Count) throw new InvalidAgentOutputException();
                var query=AgentJson.Text(call.Arguments.GetProperty("query"));
                var candidate=input.Candidates[index];
                IReadOnlyList<RetrievalResult> found;
                try { found=await retrieval.RetrieveAsync(new(query,limits.TopK,candidate.DocumentId,candidate.ManualRevisionId),RetrievalMode.Hybrid,ct); }
                catch(OperationCanceledException) when(ct.IsCancellationRequested) { throw; }
                catch(DependencyFailureException) { throw; }
                catch(UnauthorizedAccessException) { throw; }
                catch { throw new AgentDependencyException(); }
                ct.ThrowIfCancellationRequested();
                if(found is null || found.Count>limits.TopK) throw new InvalidAgentOutputException();
                foreach(var item in found)
                {
                    if(item is null || item.DocumentId!=candidate.DocumentId || item.ManualRevisionId!=candidate.ManualRevisionId) throw new InvalidAgentOutputException();
                    catalog.Add(new(item.DocumentId,item.ManualRevisionId,item.ChunkId,item.Locator,item.Snippet));
                }
                return JsonSerializer.Serialize(catalog.Describe());
            },cancellationToken);
            using var document=AgentJson.Parse(text); var root=document.RootElement; var outcome=AgentJson.Outcome(root);
            if(outcome!=AgentOutcome.Success)
            {
                AgentJson.Shape(root,"outcome");
                return outcome==AgentOutcome.InsufficientEvidence ? SymptomMatchResult.InsufficientEvidence("Grounding is insufficient.") : SymptomMatchResult.CannotProceed("Matching cannot proceed.");
            }
            AgentJson.Shape(root,"outcome","candidate","symptoms");
            var selected=AgentJson.Number(root.GetProperty("candidate"));
            if(selected<0 || selected>=input.Candidates.Count) throw new InvalidAgentOutputException();
            var symptoms=AgentJson.Array(root.GetProperty("symptoms")).Select(s=>{
                AgentJson.Shape(s,"description","evidence");
                return new MatchedSymptom(AgentJson.Text(s.GetProperty("description")),catalog.Resolve(s.GetProperty("evidence")));
            }).ToArray();
            return SymptomMatchResult.Success(new(input.Candidates[selected],symptoms));
        }
        catch(AgentExecutionLimitException) { return SymptomMatchResult.CannotProceed("Matching budget exhausted."); }
        catch(Exception e) when(AgentJson.IsInvalid(e)) { return SymptomMatchResult.CannotProceed("Invalid matching output."); }
    }
}
