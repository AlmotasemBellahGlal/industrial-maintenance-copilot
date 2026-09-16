using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Application.Abstractions.AI.Models;
namespace IndustrialCopilot.Application.Tests.Abstractions.Usage;
public class UsagePricingTests
{
    [Fact]public void PricingIsExplicitVersionedAndUsesDecimalUnitsWithoutCurrencyMixing()
    {
        var date=DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var old=new UsagePrice("host","model","USD","v1",date,1000000,1.5m,4m);
        var newer=new UsagePrice("host","model","EUR","v2",date.AddDays(1),1000000,2m,5m);
        var prices=new UsagePricing([old,newer]);
        Assert.Equal(0.0000125m,prices.Find("host","model",date)!.Estimate(new(3,2,5)).Amount);
        Assert.Equal("v2",prices.Find("host","model",date.AddDays(2))!.Version);
        Assert.Null(prices.Find("unknown","model",date));Assert.Null(prices.Find("host","model",date.AddDays(-1)));
        Assert.Throws<ArgumentException>(()=>new UsagePricing([old,old]));
        Assert.Throws<ArgumentException>(()=>new UsagePrice("host","model","USD","v",date,0,1,1));
    }
    [Fact]public async Task AmbientContextIsIsolatedAcrossConcurrentCallsAndRestored()
    {
        var outer=new UsageContext("owner",Guid.NewGuid());using var scope=new LlmCallScope(outer);
        async Task<string> Work(string actor){using var inner=new LlmCallScope(outer with {Actor=actor},true);await Task.Yield();Assert.True(LlmCallScope.OwnsRetries);return LlmCallScope.Current!.Actor;}
        Assert.Equal(new[]{"a","b"},await Task.WhenAll(Work("a"),Work("b")));Assert.Same(outer,LlmCallScope.Current);Assert.False(LlmCallScope.OwnsRetries);
    }
    [Fact]public void UnknownUsageIsDifferentFromMeasuredZero()
    {
        Assert.Null(new CompletionResponse("answer",null).Usage);
        Assert.Equal(0,new CompletionResponse("answer",new(0,0,0)).Usage!.TotalTokens);
        Assert.Null(new EmbeddingResult([new float[]{1}]).Usage);
    }
}
