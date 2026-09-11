using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>Known usage across distinct Llm attempts; missing counts prevent mistaking partial sums for complete totals.</summary>
public sealed record RunUsageSummary
{
    public TokenUsage KnownTokenUsage { get; }
    public int UnreportedTokenCallCount { get; }
    public IReadOnlyList<MonetaryCost> KnownCostsByCurrency { get; }
    public int UnpricedCallCount { get; }

    private RunUsageSummary(TokenUsage tokens, int unreported, MonetaryCost[] costs, int unpriced)
    {
        KnownTokenUsage = tokens;
        UnreportedTokenCallCount = unreported;
        KnownCostsByCurrency = Array.AsReadOnly(costs);
        UnpricedCallCount = unpriced;
    }

    /// <summary>Sums known values once per step identity, grouping costs by currency without conversion.</summary>
    /// <remarks>Overflow throws rather than wrapping; token totals retain the existing TokenUsage integer limits.</remarks>
    public static RunUsageSummary Aggregate(IReadOnlyList<TraceStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var snapshot = steps.ToArray();
        if (snapshot.Any(s => s is null) || snapshot.Select(s => s.StepId).Distinct().Count() != snapshot.Length)
            throw new ArgumentException("Steps must be non-null and have unique identities.", nameof(steps));
        var calls = snapshot.Where(s => s.Kind == TraceOperationKind.Llm).ToArray();
        var prompt = 0;
        var completion = 0;
        foreach (var call in calls)
        {
            prompt = checked(prompt + (call.Usage?.PromptTokens ?? 0));
            completion = checked(completion + (call.Usage?.CompletionTokens ?? 0));
        }
        var costs = calls.Where(s => s.Cost is not null).Select(s => s.Cost!)
            .GroupBy(c => c.Currency).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Aggregate((left, right) => left.Add(right))).ToArray();
        return new(new(prompt, completion, checked(prompt + completion)), calls.Count(s => s.Usage is null),
            costs, calls.Count(s => s.Cost is null));
    }
}
