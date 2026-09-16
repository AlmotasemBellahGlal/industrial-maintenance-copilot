using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Tracing.Models;
namespace IndustrialCopilot.Application.Abstractions.Usage;

/// <summary>Explicit configured rates per token unit; never a claim of a provider invoice.</summary>
public sealed record UsagePrice
{
    public string Provider { get; }
    public string Model { get; }
    public string Currency { get; }
    public string Version { get; }
    public DateTimeOffset EffectiveFrom { get; }
    public long TokensPerUnit { get; }
    public decimal InputRate { get; }
    public decimal OutputRate { get; }
    public UsagePrice(string provider,string model,string currency,string version,DateTimeOffset effectiveFrom,
        long tokensPerUnit,decimal inputRate,decimal outputRate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider); ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        _ = new MonetaryCost(0,currency);
        if(provider.Length>64 || model.Length>200 || version.Length>100 || effectiveFrom==default
            || tokensPerUnit<=0 || inputRate<0 || outputRate<0) throw new ArgumentException("Invalid pricing rule.");
        Provider=provider;Model=model;Currency=currency;Version=version;EffectiveFrom=effectiveFrom;
        TokensPerUnit=tokensPerUnit;InputRate=inputRate;OutputRate=outputRate;
    }
    public MonetaryCost Estimate(TokenUsage tokens) => new(checked((tokens.PromptTokens*InputRate + tokens.CompletionTokens*OutputRate)/TokensPerUnit),Currency);
}
public sealed class UsagePricing
{
    private readonly UsagePrice[] prices;
    public UsagePricing(IEnumerable<UsagePrice>? prices = null)
    {
        this.prices = prices?.ToArray() ?? [];
        if(this.prices.Any(p=>p is null) || this.prices.Select(p=>(p.Provider,p.Model,p.EffectiveFrom)).Distinct().Count()!=this.prices.Length)
            throw new ArgumentException("Ambiguous pricing rules.");
    }
    public UsagePrice? Find(string provider,string model,DateTimeOffset started) => prices
        .Where(p=>p.Provider==provider && p.Model==model && p.EffectiveFrom<=started)
        .OrderByDescending(p=>p.EffectiveFrom).FirstOrDefault();
}
