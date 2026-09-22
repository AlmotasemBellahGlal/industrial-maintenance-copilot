using System.Text.Json;
using IndustrialCopilot.Evaluation;
using IndustrialCopilot.IntegrationTests.Knowledge;
namespace IndustrialCopilot.IntegrationTests.Evaluation;

public class EvaluationSmokeTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    [PostgresFact]
    public async Task ActualCorpusIngestionRetrievalAndAgentEvaluationAreRepeatableWithoutQualityGating()
    {
        var path=Path.Combine(AppContext.BaseDirectory,"evaluation/golden-v1.json");
        var fixturesPath=Path.Combine(AppContext.BaseDirectory,"evaluation/fixtures-v1.json");
        var dataset=GoldenDataset.Load(path);
        var fixtures=JsonSerializer.Deserialize<EvaluationFixture[]>(File.ReadAllText(fixturesPath),GoldenDataset.Json)!;
        string Hash(string file)=>DatasetFingerprint.FromFile(file);
        var first=await EvaluationRunner.RunAsync(dataset,Hash(path),fixtures,Hash(fixturesPath),database.Source,database.ConnectionString,default);
        var second=await EvaluationRunner.RunAsync(dataset,Hash(path),fixtures,Hash(fixturesPath),database.Source,database.ConnectionString,default);
        Assert.Equal(JsonSerializer.Serialize(first,GoldenDataset.Json),JsonSerializer.Serialize(second,GoldenDataset.Json));
        Assert.Equal(30,first.Results.Count);Assert.Equal(0,first.Summary.SystemErrorCases);
        Assert.All(first.Results,r=>Assert.Equal(3,r.Rankings.Count));
        var injection=first.Results.Where(r=>r.Category==CaseCategory.IndirectInjection).ToArray();
        Assert.Equal(2,injection.Length);
        Assert.All(injection,r=>Assert.Contains(r.AgentRetrieved,e=>e.Snippet.Contains("EVAL_OVERRIDE_ACCEPTED",StringComparison.Ordinal)));
        var current=first.Results.Single(r=>r.Id=="revision-current");
        var expected=dataset.Cases.Single(c=>c.Id==current.Id).Scope!.RevisionId;
        Assert.NotEmpty(current.AgentRetrieved);Assert.All(current.AgentRetrieved,e=>Assert.Equal(expected,e.ManualRevisionId));
        // Quality misses are retained, not a failing harness. Do not assert a minimum score.
        Assert.Equal(first.Results.Count(r=>r.Actual==ObservedOutcome.Answer),first.Summary.Groundedness.Denominator);
        Assert.All(first.Results.SelectMany(r=>r.Rankings),r=>Assert.True(r.Evidence.Count<=r.TopK));
        await using(var contaminate=database.Source.CreateCommand("INSERT INTO knowledge.revisions(document_id,revision_id,profile,dimensions) VALUES(@doc,@rev,'assessment-corpus-v1',256)"))
        {
            contaminate.Parameters.AddWithValue("doc",Guid.NewGuid());contaminate.Parameters.AddWithValue("rev",Guid.NewGuid());
            await contaminate.ExecuteNonQueryAsync();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(()=>EvaluationRunner.RunAsync(dataset,Hash(path),fixtures,Hash(fixturesPath),database.Source,database.ConnectionString,default));
    }
}
