using System.Text.RegularExpressions;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
namespace IndustrialCopilot.Evaluation;

public enum ObservedOutcome { Answer, Refusal, Clarification, CannotProceed, SystemError }
public sealed record Ranking(RetrievalMode Mode, int TopK, IReadOnlyList<RetrievalResult> Evidence, bool? Hit, string? Error);
public sealed record CaseResult(string Id, CaseCategory Category, ExpectedBehavior Expected, Guid CorrelationId,
    IReadOnlyList<Ranking> Rankings, ObservedOutcome Actual, IReadOnlyList<string> Claims,
    IReadOnlyList<GroundedEvidence> Citations, IReadOnlyList<RetrievalResult> AgentRetrieved,
    bool? Grounded, string GroundingReason, bool RefusalExpected, bool RefusalObserved, string? Error);
public sealed record Metric(int Numerator, int Denominator, int SystemErrors)
{
    public double? Percent => Denominator == 0 ? null : Math.Round(100d * Numerator / Denominator, 4);
}
public sealed record ModeMetrics(RetrievalMode Mode, Metric HitRate, Metric HitAt1, Metric HitAt3, double? MeanReciprocalRank);
public sealed record EvaluationSummary(int Cases, int SystemErrorCases, IReadOnlyList<ModeMetrics> Retrieval,
    Metric Groundedness, Metric RefusalCorrectness, Metric FalseRefusals, Metric ClarificationCorrectness, int UnsupportedAnswersOnRefusalCases);
public static class EvaluationMetrics
{
    public static bool Matches(ExpectedEvidence expected, RetrievalResult result) => expected.DocumentId == result.DocumentId && expected.RevisionId == result.ManualRevisionId && result.Locator.StartsWith(expected.LocatorPrefix, StringComparison.Ordinal);
    public static bool Hit(GoldenCase c, IReadOnlyList<RetrievalResult> evidence, int topK)
    {
        if (topK < 1) throw new ArgumentOutOfRangeException(nameof(topK));
        return evidence.Take(topK).Any(r => c.Relevant.Any(e => Matches(e,r)));
    }
    public static bool Resolves(GroundedEvidence citation, IReadOnlyList<RetrievalResult> retrieved) => retrieved.Any(r =>
        citation.DocumentId == r.DocumentId && citation.ManualRevisionId == r.ManualRevisionId && citation.ChunkId == r.ChunkId && citation.Locator == r.Locator && citation.Snippet == r.Snippet);
    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant(), @"\s+", " ").Trim();
    public static (bool Passed, string Reason) Grounding(GoldenCase c, IReadOnlyList<string> claims, IReadOnlyList<GroundedEvidence> citations, IReadOnlyList<RetrievalResult> retrieved)
    {
        if (citations.Count == 0 || claims.Count == 0 || citations.Any(e => !Resolves(e, retrieved))) return (false,"citation_unresolved_or_missing");
        if (c.Expected != ExpectedBehavior.Answer) return (false,"answer_not_expected");
        if (!citations.Any(r => c.Relevant.Any(e => e.DocumentId == r.DocumentId && e.RevisionId == r.ManualRevisionId && r.Locator.StartsWith(e.LocatorPrefix,StringComparison.Ordinal)))) return (false,"expected_source_missing");
        var answer = Normalize(string.Join(" ", claims)); var support = Normalize(string.Join(" ", citations.Select(c => c.Snippet)));
        if (c.ForbiddenTerms.Any(t => answer.Contains(Normalize(t), StringComparison.Ordinal))) return (false,"forbidden_claim_present");
        if (c.SupportTerms.Any(t => !answer.Contains(Normalize(t),StringComparison.Ordinal) || !support.Contains(Normalize(t),StringComparison.Ordinal))) return (false,"required_fact_not_in_answer_and_evidence");
        return (true,"citation_and_expected_fact_checks_passed");
    }
    public static EvaluationSummary Summarize(GoldenDataset dataset, IReadOnlyList<CaseResult> results)
    {
        if (results.Count != dataset.Cases.Count || results.Select(r=>r.Id).Distinct().Count()!=results.Count || results.Any(r=>!dataset.Cases.Any(c=>c.Id==r.Id))) throw new ArgumentException("Missing or duplicate results.");
        var modes = new List<ModeMetrics>();
        foreach (var mode in Enum.GetValues<RetrievalMode>())
        {
            var eligible=dataset.Cases.Where(c=>c.Relevant.Count>0).Select(c=>(Case:c, Ranking:results.Single(r=>r.Id==c.Id).Rankings.Single(r=>r.Mode==mode))).ToArray();
            var executed=eligible.Where(p=>p.Ranking.Error is null).ToArray(); var errors=eligible.Length-executed.Length;
            Metric At(int k)=>new(executed.Count(p=>Hit(p.Case,p.Ranking.Evidence,Math.Min(k,p.Ranking.TopK))),executed.Length,errors);
            var mrr=executed.Length==0?(double?)null:executed.Average(p=>p.Ranking.Evidence.Take(p.Ranking.TopK).Select((r,i)=>p.Case.Relevant.Any(e=>Matches(e,r))?1d/(i+1):0).FirstOrDefault(v=>v>0));
            modes.Add(new(mode,new(executed.Count(p=>Hit(p.Case,p.Ranking.Evidence,p.Ranking.TopK)),executed.Length,errors),At(1),At(3),mrr));
        }
        var valid=results.Where(r=>r.Actual!=ObservedOutcome.SystemError).ToArray();
        Metric For(ExpectedBehavior expected, Func<CaseResult,bool> passes)=>new(valid.Count(r=>r.Expected==expected && passes(r)),valid.Count(r=>r.Expected==expected),results.Count(r=>r.Expected==expected && r.Actual==ObservedOutcome.SystemError));
        return new(results.Count,results.Count(r=>r.Error is not null || r.Rankings.Any(p=>p.Error is not null)),modes,
            new(valid.Count(r=>r.Actual==ObservedOutcome.Answer && r.Grounded==true),valid.Count(r=>r.Actual==ObservedOutcome.Answer),results.Count(r=>r.Actual==ObservedOutcome.SystemError)),
            For(ExpectedBehavior.Refusal,r=>r.Actual==ObservedOutcome.Refusal),For(ExpectedBehavior.Answer,r=>r.Actual==ObservedOutcome.Refusal),
            For(ExpectedBehavior.Clarification,r=>r.Actual==ObservedOutcome.Clarification),
            valid.Count(r=>r.Expected==ExpectedBehavior.Refusal && r.Actual==ObservedOutcome.Answer));
    }
}
