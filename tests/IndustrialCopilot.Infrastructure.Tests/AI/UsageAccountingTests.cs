using System.Runtime.CompilerServices;
using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Infrastructure.AI;
namespace IndustrialCopilot.Infrastructure.Tests.AI;
public class UsageAccountingTests
{
    private sealed class Store:ILlmUsageStore
    {
        public readonly List<LlmUsageRecord> Rows=[];public bool FailFinish;
        public Task BeginAsync(LlmUsageRecord r,CancellationToken ct){Rows.Add(r);return Task.CompletedTask;}
        public Task FinishAsync(LlmUsageRecord r,CancellationToken ct){if(FailFinish)throw new InvalidOperationException("storage unavailable");Rows[Rows.FindIndex(x=>x.CallId==r.CallId)]=r;return Task.CompletedTask;}
        public Task<UsagePage> QueryAsync(string a,IReadOnlyCollection<Guid> e,UsageQuery q,CancellationToken ct)=>throw new NotSupportedException();
    }
    private sealed class Provider:ILlmProvider
    {
        public TokenUsage? Tokens=new(4,2,6);public bool Wait;public Exception? Failure;
        public Task<CompletionResponse> CompleteAsync(CompletionRequest r,CancellationToken ct)=>Failure is {} e?Task.FromException<CompletionResponse>(e):Task.FromResult(new CompletionResponse("PRIVATE RESPONSE",Tokens));
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest r,IReadOnlyList<ToolDefinition> t,CancellationToken ct)=>Task.FromResult(new ToolCompletionResponse("",[],Tokens));
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest r,CancellationToken ct)=>Task.FromResult(new EmbeddingResult([new float[]{1}],usage:Tokens));
        public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest r,[EnumeratorCancellation]CancellationToken ct)
        {yield return new("PRIVATE RESPONSE",false);if(Wait)await Task.Delay(Timeout.Infinite,ct);yield return new("",true,Tokens);}
    }
    private static readonly UsagePricing Prices=new([new("Test","model","USD","fixture-v1",DateTimeOffset.UnixEpoch,100,1,2)]);
    [Theory][InlineData(BillingKind.Hosted,true)][InlineData(BillingKind.Local,false)][InlineData(BillingKind.Synthetic,false)]
    public async Task PhysicalCallsPreserveContextAndOnlyHostedKnownPricingProducesCost(BillingKind billing,bool priced)
    {
        var store=new Store();var context=new UsageContext("owner",Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),StepId:Guid.NewGuid());
        using var scope=new LlmCallScope(context);
        var p=new AccountedLlmProvider(new Provider(),store,Prices,"Test","model","model",billing);
        await p.CompleteAsync(Fixtures.Query,default);await p.CompleteWithToolsAsync(Fixtures.Query,[],default);await p.GenerateEmbeddingsAsync(new(["PRIVATE PROMPT"]),default);
        Assert.Equal(3,store.Rows.Select(x=>x.CallId).Distinct().Count());
        Assert.All(store.Rows,r=>{Assert.Equal(context,r.Context);Assert.Equal(UsageStatus.Succeeded,r.Status);Assert.Equal(6,r.Tokens!.TotalTokens);Assert.Equal(priced,r.EstimatedCost is not null);if(priced){Assert.Equal(.08m,r.EstimatedCost!.Amount);Assert.Equal("fixture-v1",r.PricingVersion);}});
        Assert.DoesNotContain("PRIVATE",JsonSerializer.Serialize(store.Rows));
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task UnknownUsageOrPriceNeverBecomesZero(bool unknownUsage)
    {
        var store=new Store();var inner=new Provider();if(unknownUsage)inner.Tokens=null;
        var p=new AccountedLlmProvider(inner,store,unknownUsage?Prices:new(),"Test","model","model",BillingKind.Hosted);
        await p.CompleteAsync(Fixtures.Query,default);Assert.Null(store.Rows[0].EstimatedCost);Assert.Equal(unknownUsage,store.Rows[0].Tokens is null);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task StreamRecordsProviderFinalUsageAndUnknownHonestly(bool unknown)
    {
        var store=new Store();var p=new AccountedLlmProvider(new Provider{Tokens=unknown?null:new(4,2,6)},store,Prices,"Test","model","model",BillingKind.Hosted);
        await foreach(var chunk in p.StreamAsync(Fixtures.Query,default)){}
        Assert.Equal(UsageStatus.Succeeded,store.Rows[0].Status);Assert.Equal(unknown,store.Rows[0].Tokens is null);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task PartialStreamIsIncompleteOrCancelledWithoutInventedTokens(bool cancel)
    {
        var store=new Store();var p=new AccountedLlmProvider(new Provider{Wait=true},store,Prices,"Test","model","model",BillingKind.Hosted);
        using var ct=new CancellationTokenSource();
        await using(var stream=p.StreamAsync(Fixtures.Query,ct.Token).GetAsyncEnumerator())
        {Assert.True(await stream.MoveNextAsync());if(cancel){ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await stream.MoveNextAsync());}}
        Assert.Equal(cancel?UsageStatus.Cancelled:UsageStatus.Incomplete,store.Rows[0].Status);Assert.Null(store.Rows[0].Tokens);Assert.Null(store.Rows[0].EstimatedCost);
    }
    [Fact]public async Task ManagedRetryBoundarySuppressesProviderFallbackAndRecordsFailedAttempt()
    {
        var store=new Store();var primary=new AccountedLlmProvider(new Provider{Failure=new LlmProviderException(LlmProviderFailureKind.Unavailable)},store,Prices,"Test","model","model",BillingKind.Hosted);
        var configured=new ConfiguredLlmProvider(primary,new Provider(),primary);
        using var scope=new LlmCallScope(new("owner",Guid.NewGuid()),true);
        await Assert.ThrowsAsync<LlmProviderException>(()=>configured.CompleteAsync(Fixtures.Query,default));
        Assert.Single(store.Rows);Assert.Equal(UsageStatus.Failed,store.Rows[0].Status);Assert.Null(store.Rows[0].Tokens);
    }
    [Fact]public async Task CancelledStreamPreservesCancellationEvenWhenFinalAccountingCannotBeSaved()
    {
        var store=new Store{FailFinish=true};var p=new AccountedLlmProvider(new Provider{Wait=true},store,Prices,"Test","model","model",BillingKind.Hosted);
        using var ct=new CancellationTokenSource();
        await using(var iterator=p.StreamAsync(Fixtures.Query,ct.Token).GetAsyncEnumerator())
        {Assert.True(await iterator.MoveNextAsync());ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await iterator.MoveNextAsync());}
        Assert.Equal(UsageStatus.Started,Assert.Single(store.Rows).Status);Assert.Null(store.Rows[0].Tokens);
    }
}
