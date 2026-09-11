namespace IndustrialCopilot.Application.Abstractions.Agents.SymptomMatcher;

public sealed record SymptomMatch
{
    public EquipmentManualCandidate SelectedCandidate { get; }
    public IReadOnlyList<MatchedSymptom> MatchedSymptoms { get; }
    public string? Explanation { get; }

    public SymptomMatch(EquipmentManualCandidate selectedCandidate, IReadOnlyList<MatchedSymptom> matchedSymptoms,
        string? explanation = null)
    {
        ArgumentNullException.ThrowIfNull(selectedCandidate);
        ArgumentNullException.ThrowIfNull(matchedSymptoms);
        if (explanation is not null) ArgumentException.ThrowIfNullOrWhiteSpace(explanation);
        var snapshot = matchedSymptoms.ToArray();
        if (snapshot.Length == 0 || snapshot.Any(item => item is null))
            throw new ArgumentException("At least one grounded matched symptom is required.", nameof(matchedSymptoms));
        if (snapshot.SelectMany(item => item.Evidence).Any(item => item.DocumentId != selectedCandidate.DocumentId || item.ManualRevisionId != selectedCandidate.ManualRevisionId))
            throw new ArgumentException("Evidence must match the selected document revision.", nameof(matchedSymptoms));
        SelectedCandidate = selectedCandidate;
        MatchedSymptoms = Array.AsReadOnly(snapshot);
        Explanation = explanation;
    }
}
