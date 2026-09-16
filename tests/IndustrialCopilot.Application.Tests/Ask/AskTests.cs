using System.Runtime.CompilerServices;
using IndustrialCopilot.Application.Ask;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
namespace IndustrialCopilot.Application.Tests.Ask;
public class AskTests
{
    public sealed class Retrieval : IRetrievalService
    {
        public bool Empty,Wrong; public string Snippet="pump inspection"; public readonly List<RetrievalMode> Modes=[];
        public Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery q,RetrievalMode m,CancellationToken ct)
        {Modes.Add(m);return Task.FromResult<IReadOnlyList<RetrievalResult>>(Empty?[]:[new(q.DocumentId!.Value,Wrong?Guid.NewGuid():q.ManualRevisionId!.Value,Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),"page 3",Snippet,.02)]);}
    }
    public sealed class Provider : ILlmProvider
    {
        public bool Finished,Cancelled,Fail,Wait;public CompletionRequest? Request;
        public readonly TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Stopped=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,[EnumeratorCancellation] CancellationToken ct)
        {
            Request=request;Started.TrySetResult();
            try{
                yield return new("first ",false);
                await Task.Delay(Wait?Timeout.Infinite:10,ct);
                if(Fail)throw new InvalidOperationException("PRIVATE PROVIDER FAILURE");
                yield return new("second",false);
                Finished=true;yield return new("",true);
            }finally{Cancelled=ct.IsCancellationRequested;Stopped.TrySetResult();}
        }
        public Task<CompletionResponse> CompleteAsync(CompletionRequest r,CancellationToken ct)=>throw new NotSupportedException("Streaming must not call completion.");
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest r,IReadOnlyList<ToolDefinition> t,CancellationToken ct)=>throw new NotSupportedException();
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest r,CancellationToken ct)=>Task.FromResult(new EmbeddingResult(r.Inputs.Select(_=>(IReadOnlyList<float>)new float[]{1,0}).ToArray(),"test-model"));
    }
    public static Conversation Conversation(string culture="en-US")=>new(Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),culture,DateTimeOffset.UtcNow,DateTimeOffset.UtcNow);
    [Theory][InlineData("en-US","English")][InlineData("ar-EG","Arabic")]
    public async Task StreamsBeforeCompletionAndOnlyCitesRetrievedScope(string culture,string language)
    {
        var p=new Provider();var r=new Retrieval();var c=Conversation(culture);var service=new AskService(r,p);
        await using var e=service.StreamAsync(c,new("pump"),default).GetAsyncEnumerator();
        Assert.True(await e.MoveNextAsync());Assert.Equal("evidence",e.Current.Type);
        var sources=e.Current.Citations!;Assert.Equal(c.DocumentId,Assert.Single(sources).DocumentId);
        Assert.True(await e.MoveNextAsync());Assert.Equal("first ",e.Current.Delta);Assert.False(p.Finished);
        Assert.Contains(language,p.Request!.Messages[0].Content);Assert.DoesNotContain("pump inspection",p.Request.Messages[0].Content);
        Assert.True(await e.MoveNextAsync());Assert.Equal("second",e.Current.Delta);Assert.False(p.Finished);
        Assert.True(await e.MoveNextAsync());Assert.Equal("citation",e.Current.Type);Assert.Equal(sources,e.Current.Citations);
        Assert.True(await e.MoveNextAsync());Assert.Equal(AskState.Completed,e.Current.State);Assert.False(await e.MoveNextAsync());
        Assert.Equal(new[]{RetrievalMode.Hybrid,RetrievalMode.Keyword},r.Modes);
    }
    [Fact]public async Task NoEvidenceRefusesWithoutInvokingModel(){var p=new Provider();var events=new List<AskEvent>();await foreach(var e in new AskService(new Retrieval{Empty=true},p).StreamAsync(Conversation(),new("unknown"),default))events.Add(e);Assert.Equal(AskState.InsufficientEvidence,Assert.Single(events).State);Assert.Null(p.Request);}
    [Fact]public async Task ForeignRevisionFailsBeforeCitationOrProvider(){var p=new Provider();await using var e=new AskService(new Retrieval{Wrong=true},p).StreamAsync(Conversation(),new("pump"),default).GetAsyncEnumerator();await Assert.ThrowsAsync<InvalidOperationException>(async()=>await e.MoveNextAsync());Assert.Null(p.Request);}
    [Fact]public async Task CancellationInterruptsProviderAndDisposesEnumerator(){var p=new Provider{Wait=true};using var ct=new CancellationTokenSource();await using var e=new AskService(new Retrieval(),p).StreamAsync(Conversation(),new("pump"),ct.Token).GetAsyncEnumerator();await e.MoveNextAsync();await e.MoveNextAsync();var pending=e.MoveNextAsync().AsTask();ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>pending);Assert.True(p.Cancelled);Assert.False(p.Finished);}
    [Theory][InlineData(0)][InlineData(11)]public void TopKIsBounded(int n)=>Assert.Throws<ArgumentOutOfRangeException>(()=>new AskQuestion("pump",n));
    [Fact]public void QuestionIsBounded(){Assert.Throws<ArgumentException>(()=>new AskQuestion(" "));Assert.Throws<ArgumentException>(()=>new AskQuestion(new string('q',2001)));}
    [Fact]public async Task OversizedExcerptRefusesBeforeEmittingUnboundedFrameOrCallingProvider()
    {
        var provider=new Provider();var events=new List<AskEvent>();
        await foreach(var item in new AskService(new Retrieval{Snippet=new string('x',16001)},provider).StreamAsync(Conversation(),new("pump"),default)) events.Add(item);
        Assert.Equal(AskState.InsufficientEvidence,Assert.Single(events).State);Assert.Null(provider.Request);
    }
}
