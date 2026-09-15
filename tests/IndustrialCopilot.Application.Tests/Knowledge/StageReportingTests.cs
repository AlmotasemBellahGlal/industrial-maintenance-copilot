using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;
using IndustrialCopilot.Application.Knowledge;
namespace IndustrialCopilot.Application.Tests.Knowledge;

public class StageReportingTests
{
    [Theory]
    [InlineData(IngestionStage.Extracting)]
    [InlineData(IngestionStage.Cleaning)]
    [InlineData(IngestionStage.Chunking)]
    [InlineData(IngestionStage.Embedding)]
    [InlineData(IngestionStage.Indexing)]
    public async Task StageFailureIsIdentifiableWithoutPersistingExceptionInternals(IngestionStage failure)
    {
        var fake=new Ports(failure);var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");
        var service=new ManualIngestionService(new DocumentPipeline(fake,fake,fake),fake,fake,new("profile","model",2),reports:fake);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.IngestAsync(request,Stream.Null,default));
        Assert.Equal(failure,fake.Stages.Last()); Assert.Equal(IngestionFailure.StageFailed,fake.Failure);
        Assert.Equal(Enumerable.Range(1,(int)failure).Select(i=>(IngestionStage)i),fake.Stages);
        Assert.True(fake.Disposed);
    }
    [Fact]
    public async Task UnsupportedProviderOperationIsNotMisreportedAsUnsupportedInputFormat()
    {
        var fake = new Ports(IngestionStage.Embedding, true);
        var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "text/plain");
        var service = new ManualIngestionService(new DocumentPipeline(fake, fake, fake), fake, fake, new("profile", "model", 2), reports: fake);
        await Assert.ThrowsAsync<NotSupportedException>(() => service.IngestAsync(request, Stream.Null, default));
        Assert.Equal(IngestionFailure.StageFailed, fake.Failure);
        Assert.Equal(IngestionStage.Embedding, fake.Stages.Last());
    }
    [Fact]
    public async Task SuccessPassesTheSameAttemptToAtomicIndexCompletion()
    {
        var fake=new Ports(null);var request=new DocumentProcessingRequest(Guid.NewGuid(),Guid.NewGuid(),"text/plain");
        var service=new ManualIngestionService(new DocumentPipeline(fake,fake,fake),fake,fake,new("profile","model",2),reports:fake);
        Assert.Equal(1,await service.IngestAsync(request,Stream.Null,default));
        Assert.Equal(fake.Id,fake.Indexed!.IngestionAttemptId); Assert.Null(fake.Failure); Assert.True(fake.Disposed);
    }
    private sealed class Ports(IngestionStage? failure, bool unsupported = false) : IDocumentExtractor,IDocumentCleaner,IDocumentChunker,ILlmProvider,IKnowledgeIndex,IIngestionReports,IIngestionAttempt
    {
        public Guid Id {get;}=Guid.NewGuid(); public List<IngestionStage> Stages {get;}=[IngestionStage.Extracting];
        public IngestionFailure? Failure; public bool Disposed;public RevisionIndexRequest? Indexed;
        private void Check(IngestionStage stage){if(stage==failure && unsupported)throw new NotSupportedException();if(stage==failure)throw new InvalidOperationException("secret provider response must never be persisted");}
        public Task<ExtractedDocument> ExtractAsync(DocumentProcessingRequest request,Stream source,CancellationToken token){Check(IngestionStage.Extracting);return Task.FromResult(new ExtractedDocument([new("text")],null));}
        public ExtractedDocument Clean(ExtractedDocument document,CancellationToken token){Check(IngestionStage.Cleaning);return document;}
        public IReadOnlyList<DocumentChunk> Chunk(DocumentProcessingRequest request,ExtractedDocument document,CancellationToken token){Check(IngestionStage.Chunking);return [new(request.DocumentId,request.ManualRevisionId,Guid.NewGuid(),"line 1","text")];}
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request,CancellationToken token){Check(IngestionStage.Embedding);return Task.FromResult(new EmbeddingResult([new float[]{1,0}],"model"));}
        public Task ReplaceRevisionAsync(RevisionIndexRequest request,CancellationToken token){Check(IngestionStage.Indexing);Indexed=request;return Task.CompletedTask;}
        public Task<IIngestionAttempt> BeginAsync(DocumentProcessingRequest request,CancellationToken token)=>Task.FromResult<IIngestionAttempt>(this);
        public Task<IReadOnlyList<IngestionReport>> ReadAsync(Guid document,Guid revision,CancellationToken token)=>throw new NotSupportedException();
        public Task AdvanceAsync(IngestionStage stage,int? pages,int? chunks,CancellationToken token){Stages.Add(stage);return Task.CompletedTask;}
        public Task FailAsync(IngestionFailure value,CancellationToken token){Failure=value;return Task.CompletedTask;}
        public ValueTask DisposeAsync(){Disposed=true;return ValueTask.CompletedTask;}
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,IReadOnlyList<ToolDefinition> tools,CancellationToken token)=>throw new NotSupportedException();
        public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,CancellationToken token)=>throw new NotSupportedException();
    }
}
