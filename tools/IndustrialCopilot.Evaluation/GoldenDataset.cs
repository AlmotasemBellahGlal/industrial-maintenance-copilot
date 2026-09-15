using System.Text.Json;
using System.Text.Json.Serialization;
namespace IndustrialCopilot.Evaluation;

public enum CaseCategory { Normal, DirectInjection, IndirectInjection, OutOfCorpus, Ambiguous, ConflictingRevision }
public enum ExpectedBehavior { Answer, Refusal, Clarification }
public sealed record InputScope(Guid DocumentId, Guid? RevisionId);
public sealed record ExpectedEvidence(Guid DocumentId, Guid RevisionId, string LocatorPrefix);
public sealed record GoldenCase(string Id, CaseCategory Category, string Question, InputScope? Scope,
    ExpectedBehavior Expected, IReadOnlyList<ExpectedEvidence> Relevant, IReadOnlyList<string> SupportTerms,
    IReadOnlyList<string> ForbiddenTerms, string Rationale);
public sealed record GoldenDataset(string Version, IReadOnlyList<GoldenCase> Cases)
{
    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static GoldenDataset Load(string path)
    {
        var data = JsonSerializer.Deserialize<GoldenDataset>(File.ReadAllText(path), Json) ?? throw new ArgumentException("Missing dataset.");
        data.Validate(); return data;
    }
    public void Validate()
    {
        if (Version != "fr3-golden-v1" || Cases is null || Cases.Count < 30) throw new ArgumentException("Versioned dataset requires at least 30 cases.");
        if (Cases.Any(c => c is null) || Cases.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() != Cases.Count) throw new ArgumentException("Duplicate/null cases.");
        foreach (var c in Cases)
        {
            if (string.IsNullOrWhiteSpace(c.Id) || string.IsNullOrWhiteSpace(c.Question) || string.IsNullOrWhiteSpace(c.Rationale) ||
                !Enum.IsDefined(c.Category) || !Enum.IsDefined(c.Expected) || c.Relevant is null || c.SupportTerms is null || c.ForbiddenTerms is null)
                throw new ArgumentException("Invalid case structure.");
            if (c.Scope is {} scope && (scope.DocumentId == Guid.Empty || scope.RevisionId == Guid.Empty)) throw new ArgumentException("Invalid input scope.");
            if (c.Relevant.Any(r => r is null || r.DocumentId == Guid.Empty || r.RevisionId == Guid.Empty || string.IsNullOrWhiteSpace(r.LocatorPrefix)) ||
                c.Relevant.Distinct().Count() != c.Relevant.Count || c.SupportTerms.Concat(c.ForbiddenTerms).Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Invalid evidence expectation.");
            if (c.Expected == ExpectedBehavior.Answer && (c.Relevant.Count == 0 || c.SupportTerms.Count == 0)) throw new ArgumentException("Answers need evidence and support facts.");
            if (c.Expected == ExpectedBehavior.Refusal && (c.Relevant.Count != 0 || c.SupportTerms.Count != 0)) throw new ArgumentException("Refusal expectations cannot require an answer.");
            if (c.Relevant.Any(r => c.Scope is {} input && (r.DocumentId != input.DocumentId || (input.RevisionId is {} revision && r.RevisionId != revision)))) throw new ArgumentException("Expected evidence contradicts explicit input scope.");
        }
        if (Cases.Count(c => c.Category == CaseCategory.Normal) < 18 || Cases.Count(c => c.Category != CaseCategory.Normal) < 5 ||
            Cases.Count(c => c.Category is CaseCategory.DirectInjection or CaseCategory.IndirectInjection) < 5 ||
            Cases.Count(c => c.Category == CaseCategory.OutOfCorpus) < 3 || Cases.Count(c => c.Category == CaseCategory.Ambiguous) < 2 ||
            Cases.Count(c => c.Category == CaseCategory.ConflictingRevision) < 2 || !Cases.Any(c => c.Category == CaseCategory.DirectInjection) || !Cases.Any(c => c.Category == CaseCategory.IndirectInjection))
            throw new ArgumentException("Missing required category coverage.");
    }
}
public sealed record EvaluationFixture(Guid DocumentId, Guid RevisionId, int RevisionNumber, string Title, string Text);
