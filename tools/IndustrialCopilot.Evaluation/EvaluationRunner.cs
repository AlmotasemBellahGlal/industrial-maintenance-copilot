using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
using IndustrialCopilot.Application.Knowledge;
using IndustrialCopilot.Application.Reasoning;
using IndustrialCopilot.Corpus;
using IndustrialCopilot.Infrastructure.Knowledge;
using Npgsql;
namespace IndustrialCopilot.Evaluation;

public sealed record EvaluationReport(string DatasetVersion, string DatasetSha256, string FixtureSha256,
    string Configuration, EvaluationSummary Summary, IReadOnlyList<CaseResult> Results);
public static class EvaluationRunner
{
    public static async Task<EvaluationReport> RunAsync(GoldenDataset dataset, string datasetHash, IReadOnlyList<EvaluationFixture> fixtures,
        string fixtureHash, NpgsqlDataSource source, string connectionString, CancellationToken token)
    {
        dataset.Validate();
        var corpus=AssessmentCorpus.Generate(); AssessmentCorpus.Validate(corpus);
        var catalog=corpus.Select(c=>(Doc:c.Request.DocumentId,Rev:c.Request.ManualRevisionId)).Concat(fixtures.Select(f=>(Doc:f.DocumentId,Rev:f.RevisionId))).ToArray();
        if (catalog.Distinct().Count()!=catalog.Length || fixtures.Any(f=>f.DocumentId==Guid.Empty || f.RevisionId==Guid.Empty || f.RevisionNumber<1 || string.IsNullOrWhiteSpace(f.Title) || string.IsNullOrWhiteSpace(f.Text))) throw new ArgumentException("Invalid evaluation fixtures.");
        foreach(var c in dataset.Cases)
            if(c.Relevant.Any(e=>!catalog.Contains((e.DocumentId,e.RevisionId))) || (c.Scope is {} scope && !catalog.Any(k=>k.Doc==scope.DocumentId && (scope.RevisionId is null || k.Rev==scope.RevisionId)))) throw new ArgumentException("Unknown dataset revision identity.");
        await new KnowledgeSchema(source).ApplyAsync(token);
        var embeddings=new CorpusEmbeddings(); var space=new EmbeddingSpace("assessment-corpus-v1","synthetic-lexical-v1",32);
        var store=new PostgresKnowledgeStore(source,new(space,"synthetic-lexical-v1",100,0),embeddings);
        await using var reports=new PostgresIngestionReports(connectionString);
        var ingestion=new ManualIngestionService(new DocumentPipeline(new ManualDocumentExtractor(),new DocumentCleaner(),new DeterministicDocumentChunker()),embeddings,store,space,reports:reports);
        foreach(var document in corpus)
        {
            using var stream=new MemoryStream(document.Bytes);
            await ingestion.IngestAsync(document.Request,stream,token);
        }
        foreach(var fixture in fixtures)
        {
            using var stream=new MemoryStream(Encoding.UTF8.GetBytes(fixture.Text));
            await ingestion.IngestAsync(new(fixture.DocumentId,fixture.RevisionId,"text/plain",new(fixture.Title,"synthetic:evaluation-only",fixture.RevisionNumber)),stream,token);
        }
        // A baseline must not silently include stale/foreign revisions from another experiment.
        var indexed=new HashSet<(Guid Doc,Guid Rev)>();
        await using(var command=source.CreateCommand("SELECT document_id,revision_id FROM knowledge.revisions WHERE profile=@profile"))
        {
            command.Parameters.AddWithValue("profile",space.Profile);
            await using var reader=await command.ExecuteReaderAsync(token);
            while(await reader.ReadAsync(token))indexed.Add((reader.GetGuid(0),reader.GetGuid(1)));
        }
        if(!indexed.SetEquals(catalog))throw new InvalidOperationException("Evaluation database contains unrelated revisions.");
        var results=new List<CaseResult>();
        foreach(var c in dataset.Cases)
        {
            token.ThrowIfCancellationRequested();
            // Only question/scope enter the actual retrieval and agent paths. Golden labels are scored afterward.
            var observed=await ObserveAsync(c.Question,c.Scope,catalog,store,token);
            var rankings=observed.Rankings.Select(r=>r with {Hit=r.Error is null && c.Relevant.Count>0 ? EvaluationMetrics.Hit(c,r.Evidence,r.TopK) : null}).ToArray();
            var grounding=observed.Outcome==ObservedOutcome.Answer?EvaluationMetrics.Grounding(c,observed.Claims,observed.Citations,observed.AgentRetrieved):(false,"no_answer");
            var correlation=new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(dataset.Version+":"+c.Id)).AsSpan(0,16));
            results.Add(new(c.Id,c.Category,c.Expected,correlation,rankings,observed.Outcome,observed.Claims,observed.Citations,observed.AgentRetrieved,
                observed.Outcome==ObservedOutcome.Answer?grounding.Item1:null,grounding.Item2,c.Expected==ExpectedBehavior.Refusal,observed.Outcome==ObservedOutcome.Refusal,observed.Error));
        }
        return new(dataset.Version,datasetHash,fixtureHash,
            "fr1-v1 + 3 evaluation-only text revisions; assessment-corpus-v1 / synthetic-lexical-v1 / dimensions=32; TopK=5; cosine>=0; RRF k=60 candidates=100; agent=production SymptomMatcherAgent + unchanged DemoProvider; agent retrieval=Hybrid",
            EvaluationMetrics.Summarize(dataset,results),results);
    }
    private sealed record Observation(IReadOnlyList<Ranking> Rankings,ObservedOutcome Outcome,IReadOnlyList<string> Claims,IReadOnlyList<GroundedEvidence> Citations,IReadOnlyList<RetrievalResult> AgentRetrieved,string? Error);
    private static async Task<Observation> ObserveAsync(string question,InputScope? scope,(Guid Doc,Guid Rev)[] catalog,IRetrievalService retrieval,CancellationToken token)
    {
        var rankings=new List<Ranking>();
        foreach(var mode in Enum.GetValues<RetrievalMode>())
        {
            try { rankings.Add(new(mode,5,await retrieval.RetrieveAsync(new(question,5,scope?.DocumentId,scope?.RevisionId),mode,token),null,null)); }
            catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
            catch { rankings.Add(new(mode,5,[],null,"retrieval_failed")); }
        }
        if(rankings.Any(r=>r.Error is not null))return new(rankings,ObservedOutcome.SystemError,[],[],[],"retrieval_failed");
        var first=rankings.Single(r=>r.Mode==RetrievalMode.Hybrid).Evidence.FirstOrDefault();
        // Test-host candidate selection is ranking-based, never based on the expected answer.
        var candidate=first is null?catalog.First(c=>scope is null || (c.Doc==scope.DocumentId && (scope.RevisionId is null || c.Rev==scope.RevisionId))):(first.DocumentId,first.ManualRevisionId);
        var recording=new RecordedRetrieval(retrieval);
        try
        {
            var agent=new SymptomMatcherAgent(new IndustrialCopilot.Demo.DemoProvider(),recording,new(topK:5));
            var result=await agent.MatchAsync(new(question,[new(candidate.Item1,"evaluation input candidate",candidate.Item1,candidate.Item2)],[]),token);
            var outcome=result.Outcome switch {AgentOutcome.Success=>ObservedOutcome.Answer,AgentOutcome.InsufficientEvidence=>ObservedOutcome.Refusal,_=>ObservedOutcome.CannotProceed};
            return new(rankings,outcome,result.Match?.MatchedSymptoms.Select(s=>s.Description).ToArray()??[],
                result.Match?.MatchedSymptoms.SelectMany(s=>s.Evidence).Distinct().ToArray()??[],recording.Evidence,null);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){throw;}
        catch { return new(rankings,ObservedOutcome.SystemError,[],[],recording.Evidence,"agent_dependency_failed"); }
    }
    private sealed class RecordedRetrieval(IRetrievalService inner):IRetrievalService
    {
        public List<RetrievalResult> Evidence {get;}=[];
        public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery query,RetrievalMode mode,CancellationToken token)
        {
            var found=await inner.RetrieveAsync(query,mode,token);Evidence.AddRange(found);return found;
        }
    }
    public static string Markdown(EvaluationReport report)
    {
        string Rate(Metric metric)=>$"{metric.Numerator}/{metric.Denominator} ({metric.Percent?.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture)??"N/A"}%), errors={metric.SystemErrors}";
        var lines=new List<string>{"# Deterministic FR-3 baseline", "", $"Dataset SHA256: `{report.DatasetSha256}`", $"Configuration: {report.Configuration}", "", $"Cases: {report.Summary.Cases}; system-error cases: {report.Summary.SystemErrorCases}", ""};
        foreach(var mode in report.Summary.Retrieval)lines.Add($"- {mode.Mode} Hit@5: {Rate(mode.HitRate)}; Hit@1: {Rate(mode.HitAt1)}; Hit@3: {Rate(mode.HitAt3)}; MRR: {mode.MeanReciprocalRank?.ToString("0.####",System.Globalization.CultureInfo.InvariantCulture)??"N/A"}");
        lines.AddRange([$"- Groundedness proxy: {Rate(report.Summary.Groundedness)}",$"- Refusal correctness: {Rate(report.Summary.RefusalCorrectness)}",$"- False refusals: {Rate(report.Summary.FalseRefusals)}",$"- Clarification correctness: {Rate(report.Summary.ClarificationCorrectness)}",$"- Unsupported answers on refusal cases: {report.Summary.UnsupportedAnswersOnRefusalCases}","","| Case | Expected | Actual | Hybrid hit | Grounding |","|---|---|---|---|---|"]);
        foreach(var r in report.Results)lines.Add($"| {r.Id} | {r.Expected} | {r.Actual} | {r.Rankings.Single(x=>x.Mode==RetrievalMode.Hybrid).Hit?.ToString()??"N/A"} | {r.GroundingReason} |");
        return string.Join("\n",lines)+"\n";
    }
}
