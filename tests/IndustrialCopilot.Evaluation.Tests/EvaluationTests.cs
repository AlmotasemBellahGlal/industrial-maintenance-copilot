using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
namespace IndustrialCopilot.Evaluation.Tests;

public class EvaluationTests
{
    private static readonly Guid Doc=Guid.Parse("00000000-0000-0000-0000-000000000001"),Rev=Guid.Parse("00000000-0000-0000-0000-000000000002"),Chunk=Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static GoldenDataset Golden()=>GoldenDataset.Load(Path.Combine(AppContext.BaseDirectory,"evaluation/golden-v1.json"));
    private static GoldenCase Case(string id="a",ExpectedBehavior expected=ExpectedBehavior.Answer)=>new(id,CaseCategory.Normal,"seal",null,expected,expected==ExpectedBehavior.Answer?[new(Doc,Rev,"line 1")]:[],expected==ExpectedBehavior.Answer?["seal"]:[],["forbidden"],"test rationale");
    private static RetrievalResult Evidence=>new(Doc,Rev,Chunk,"line 1","seal evidence",1);
    private static GroundedEvidence Citation=>new(Doc,Rev,Chunk,"line 1","seal evidence");
    private static CaseResult Result(GoldenCase c,ObservedOutcome outcome, bool? grounded=null)=>new(c.Id,c.Category,c.Expected,Doc,
        Enum.GetValues<RetrievalMode>().Select(m=>new Ranking(m,5,outcome==ObservedOutcome.SystemError?[]:[Evidence],null,outcome==ObservedOutcome.SystemError?"retrieval_failed":null)).ToArray(),
        outcome,[],[],[],grounded,"test",c.Expected==ExpectedBehavior.Refusal,outcome==ObservedOutcome.Refusal,outcome==ObservedOutcome.SystemError?"system_failed":null);
    [Fact]
    public void GoldenFileIsFrozenHasRequiredCountsAndAdversarialCoverage()
    {
        var data=Golden();Assert.Equal(30,data.Cases.Count);Assert.Equal(18,data.Cases.Count(c=>c.Category==CaseCategory.Normal));
        Assert.Equal(12,data.Cases.Count(c=>c.Category!=CaseCategory.Normal));
        Assert.Equal(3,data.Cases.Count(c=>c.Category==CaseCategory.DirectInjection));Assert.Equal(2,data.Cases.Count(c=>c.Category==CaseCategory.IndirectInjection));
        Assert.Equal(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"evaluation/golden-v1.sha256")).Trim(),DatasetFingerprint.FromFile(Path.Combine(AppContext.BaseDirectory,"evaluation/golden-v1.json")));
    }
    [Fact]
    public void FingerprintIgnoresCheckoutLineEndingsButDetectsContentChanges()
    {
        Assert.Equal(DatasetFingerprint.Compute("a\nb\n"), DatasetFingerprint.Compute("a\r\nb\r\n"));
        Assert.NotEqual(DatasetFingerprint.Compute("a\nb\n"), DatasetFingerprint.Compute("a\nc\n"));
    }
    [Theory]
    [InlineData("count")][InlineData("duplicates")][InlineData("coverage")][InlineData("expectation")][InlineData("evidence")][InlineData("null_collection")]
    public void InvalidDatasetsAreRejected(string mutation)
    {
        var data=Golden();var cases=data.Cases.ToArray();
        switch(mutation)
        {
            case "count":cases=cases.Take(24).ToArray();break;
            case "duplicates":cases[1]=cases[0];break;
            case "coverage":cases=cases.Select(c=>c with{Category=CaseCategory.Normal}).ToArray();break;
            case "expectation":cases[0]=cases[0] with{Expected=(ExpectedBehavior)99};break;
            case "evidence":cases[0]=cases[0] with{Relevant=[]};break;
            case "null_collection":cases[0]=cases[0] with{SupportTerms=null!};break;
        }
        Assert.Throws<ArgumentException>(()=>(data with{Cases=cases}).Validate());
    }
    [Fact]
    public void InputScopeCannotContradictExpectedRevision()
    {
        var data=Golden();var cases=data.Cases.ToArray();cases[0]=cases[0] with{Scope=new(Doc,Rev)};
        Assert.Throws<ArgumentException>(()=>(data with{Cases=cases}).Validate());
    }
    [Fact]
    public void TopKAndRevisionAndLocatorAllMatterForHitRate()
    {
        var c=Case();var wrong=new RetrievalResult(Doc,Guid.NewGuid(),Chunk,"line 1","seal",1);
        Assert.False(EvaluationMetrics.Hit(c,[wrong,Evidence],1));Assert.True(EvaluationMetrics.Hit(c,[wrong,Evidence],2));
        Assert.False(EvaluationMetrics.Hit(c,[new(Doc,Rev,Chunk,"line 2","seal",1)],5));
        Assert.Throws<ArgumentOutOfRangeException>(()=>EvaluationMetrics.Hit(c,[Evidence],0));
    }
    [Fact]
    public void GroundingRequiresResolvingCitationAndFactInBothAnswerAndEvidence()
    {
        Assert.True(EvaluationMetrics.Grounding(Case(),["Check the seal"],[Citation],[Evidence]).Passed);
        Assert.False(EvaluationMetrics.Grounding(Case(),["Check the motor"],[Citation],[Evidence]).Passed);
        Assert.False(EvaluationMetrics.Grounding(Case(),["Check the seal"],[],[Evidence]).Passed);
        Assert.False(EvaluationMetrics.Grounding(Case(),["forbidden seal"],[Citation],[Evidence]).Passed);
        Assert.False(EvaluationMetrics.Grounding(Case(),["seal"],[new(Doc,Rev,Chunk,"line 1","forged snippet")],[Evidence]).Passed);
    }
    [Fact]
    public void WhitespaceVariationDoesNotRequireExactProse()
    {
        Assert.True(EvaluationMetrics.Grounding(Case() with{SupportTerms=["seal evidence"]},["This SEAL\n evidence matters."],[Citation],[Evidence]).Passed);
    }
    [Fact]
    public void SystemErrorsAreSeparateFromMissesAndCannotBecomeCorrectRefusals()
    {
        var answer=Case();var refusal=Case("b",ExpectedBehavior.Refusal);var failed=Case("c",ExpectedBehavior.Refusal);
        var data=new GoldenDataset("test",[answer,refusal,failed]);
        var summary=EvaluationMetrics.Summarize(data,[Result(answer,ObservedOutcome.Refusal),Result(refusal,ObservedOutcome.Answer,false),Result(failed,ObservedOutcome.SystemError)]);
        Assert.Equal(new Metric(0,1,1),summary.RefusalCorrectness);Assert.Equal(new Metric(1,1,0),summary.FalseRefusals);
        Assert.Equal(new Metric(0,1,1),summary.Groundedness);Assert.Equal(1,summary.SystemErrorCases);Assert.Equal(1,summary.UnsupportedAnswersOnRefusalCases);
    }
    [Fact]
    public void ZeroDenominatorIsNotReportedAsPerfectQuality()
    {
        var c=Case();var summary=EvaluationMetrics.Summarize(new("test",[c]),[Result(c,ObservedOutcome.SystemError)]);
        Assert.Null(summary.Groundedness.Percent);Assert.Null(summary.Retrieval[0].HitRate.Percent);Assert.Null(summary.RefusalCorrectness.Percent);
    }
    [Fact]
    public void HitRateAndReciprocalRankUseActualRankAndEligibleCases()
    {
        var a=Case();var b=Case("b");var refusal=Case("r",ExpectedBehavior.Refusal);
        var wrong=new RetrievalResult(Guid.NewGuid(),Rev,Chunk,"line 1","wrong",1);
        var ranked=Result(a,ObservedOutcome.Answer,true) with{Rankings=Enum.GetValues<RetrievalMode>().Select(m=>new Ranking(m,5,[wrong,Evidence],true,null)).ToArray()};
        var miss=Result(b,ObservedOutcome.Refusal) with{Rankings=Enum.GetValues<RetrievalMode>().Select(m=>new Ranking(m,5,[],false,null)).ToArray()};
        var summary=EvaluationMetrics.Summarize(new("test",[a,b,refusal]),[ranked,miss,Result(refusal,ObservedOutcome.Refusal)]);
        Assert.All(summary.Retrieval,m=>{Assert.Equal(50,m.HitRate.Percent);Assert.Equal(0,m.HitAt1.Percent);Assert.Equal(.25,m.MeanReciprocalRank);});
        Assert.Equal(100,summary.RefusalCorrectness.Percent);
    }
    [Fact]
    public void MissingResultsCannotSilentlyInflateScores()
    {Assert.Throws<ArgumentException>(()=>EvaluationMetrics.Summarize(new("test",[Case()]),[]));}
    [Fact]
    public void MachineResultsRoundTripWithoutLosingSafeErrorOrCitation()
    {
        var result=Result(Case(),ObservedOutcome.Answer,true) with{Claims=["seal"],Citations=[Citation],AgentRetrieved=[Evidence]};
        var copy=JsonSerializer.Deserialize<CaseResult>(JsonSerializer.Serialize(result,GoldenDataset.Json),GoldenDataset.Json)!;
        Assert.Equal(result.Id,copy.Id);Assert.Equal(result.Citations,copy.Citations);Assert.Equal(result.Rankings[0].Evidence,copy.Rankings[0].Evidence);
    }
    [Theory]
    [InlineData(CaseCategory.OutOfCorpus,ExpectedBehavior.Refusal)]
    [InlineData(CaseCategory.Ambiguous,ExpectedBehavior.Clarification)]
    [InlineData(CaseCategory.DirectInjection,ExpectedBehavior.Refusal)]
    [InlineData(CaseCategory.IndirectInjection,ExpectedBehavior.Answer)]
    public void AdversarialExpectationsAreExplicit(CaseCategory category,ExpectedBehavior expected)
    {Assert.All(Golden().Cases.Where(c=>c.Category==category),c=>Assert.Equal(expected,c.Expected));}
    [Fact]
    public void ConflictingRevisionsIncludeCurrentScopeAndUnspecifiedClarification()
    {
        var cases=Golden().Cases.Where(c=>c.Category==CaseCategory.ConflictingRevision).ToArray();
        Assert.Contains(cases,c=>c.Scope!.RevisionId is not null && c.Expected==ExpectedBehavior.Answer);
        Assert.Contains(cases,c=>c.Scope!.RevisionId is null && c.Expected==ExpectedBehavior.Clarification && c.Relevant.Count==2);
    }
}
