using IndustrialCopilot.Application.Abstractions.Usage;
using IndustrialCopilot.Infrastructure.Operations;
using IndustrialCopilot.IntegrationTests.Knowledge;
namespace IndustrialCopilot.IntegrationTests.Operations;
public class UsageStoreTests(KnowledgeDatabase database):IClassFixture<KnowledgeDatabase>
{
    [PostgresFact]public async Task RestartIdempotencyOwnershipPagingAndUnknownAwareAggregation()
    {
        await new OperationalSchema(database.Source).ApplyAsync(default);
        var store=new PostgresLlmUsageStore(database.Source);var equipment=Guid.NewGuid();var correlation=Guid.NewGuid();var run=Guid.NewGuid();
        var start=new LlmUsageRecord(Guid.NewGuid(),new("owner",correlation,equipment,run),"Test","model",LlmOperation.Streaming,BillingKind.Hosted,DateTimeOffset.UtcNow);
        await Task.WhenAll(store.BeginAsync(start,default),store.BeginAsync(start,default));
        var end=start with{Status=UsageStatus.Succeeded,CompletedAt=start.StartedAt.AddSeconds(1),Tokens=new(4,2,6),EstimatedCost=new(.08m,"USD"),PricingVersion="fixture-v1"};
        await Task.WhenAll(store.FinishAsync(end,default),store.FinishAsync(end,default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.FinishAsync(end with{Status=UsageStatus.Failed},default));
        await Assert.ThrowsAsync<InvalidOperationException>(()=>store.BeginAsync(start with{Context=start.Context with{Actor="intruder"}},default));
        await store.BeginAsync(start with{CallId=Guid.NewGuid()},default); // crash leaves unknown Started
        await store.BeginAsync(start with{CallId=Guid.NewGuid(),Context=start.Context with{Actor="other"}},default);
        await store.BeginAsync(start with{CallId=Guid.NewGuid(),Context=start.Context with{EquipmentId=Guid.NewGuid()}},default);
        store=new(database.Source);
        var page=await store.QueryAsync("owner",[equipment],new(CorrelationId:correlation,RunId:run,Provider:"Test",From:start.StartedAt.AddSeconds(-1),Until:start.StartedAt.AddSeconds(2),Limit:1),default);
        Assert.Single(page.Records);Assert.Equal(2,page.Summary.Calls);Assert.Equal(1,page.Summary.KnownTokenCalls);Assert.Equal(6,page.Summary.TotalTokens);Assert.Equal(1,page.Summary.UnavailableTokenCalls);Assert.Equal(1,page.Summary.UnpricedHostedCalls);Assert.Equal(.08m,Assert.Single(page.Summary.KnownEstimatedCosts).Amount);
        var next=await store.QueryAsync("owner",[equipment],new(CorrelationId:correlation,Offset:1,Limit:1),default);Assert.NotEqual(page.Records[0].CallId,Assert.Single(next.Records).CallId);
        Assert.Empty((await store.QueryAsync("intruder",[equipment],new(RunId:run),default)).Records);
        Assert.Empty((await store.QueryAsync("owner",[],new(),default)).Records);
        Assert.Empty((await store.QueryAsync("owner",[equipment],new(Provider:"Other"),default)).Records);
        Assert.Null((await store.QueryAsync("other",[equipment],new(),default)).Summary.TotalTokens);
        await Assert.ThrowsAsync<ArgumentException>(()=>store.QueryAsync("owner",[equipment],new(Limit:101),default));
    }
}
