using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;
using IndustrialCopilot.Application.Knowledge;

namespace IndustrialCopilot.Application.Tests.Knowledge;

public class IngestionTests
{
    private sealed class Processor(IReadOnlyList<DocumentChunk> chunks) : IDocumentProcessor
    {
        public Task<IReadOnlyList<DocumentChunk>> ProcessAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken) => Task.FromResult(chunks);
    }
    private sealed class Index : IKnowledgeIndex
    {
        internal RevisionIndexRequest? Saved;
        public Task ReplaceRevisionAsync(RevisionIndexRequest request, CancellationToken cancellationToken) { Saved = request; return Task.CompletedTask; }
    }
    private sealed class Embeddings : ILlmProvider
    {
        internal int Calls;
        internal Func<int, EmbeddingResult>? Result;
        internal CancellationToken SeenToken;
        public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
        {
            Calls++; SeenToken = cancellationToken;
            return Task.FromResult(Result?.Invoke(Calls) ?? new EmbeddingResult(request.Inputs.Select(_ => (IReadOnlyList<float>)new float[] { 1, 0 }).ToArray(), "model-v1"));
        }
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
        public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken token) => throw new NotSupportedException();
        public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
    }

    [Fact]
    public async Task PublishesOnceAfterAllEmbeddingBatchesAndPropagatesCancellation()
    {
        var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "text/plain");
        var chunks = Enumerable.Range(0, 3).Select(i => new DocumentChunk(request.DocumentId, request.ManualRevisionId, Guid.NewGuid(), $"line {i}", $"text {i}")).ToArray();
        var index = new Index(); var embeddings = new Embeddings();
        using var cancellation = new CancellationTokenSource();
        var service = new ManualIngestionService(new Processor(chunks), embeddings, index, new("profile", "model-v1", 2), 2);
        Assert.Equal(3, await service.IngestAsync(request, Stream.Null, cancellation.Token));
        Assert.Equal(2, embeddings.Calls);
        Assert.Equal(cancellation.Token, embeddings.SeenToken);
        Assert.Equal("profile", index.Saved!.EmbeddingProfile);
        Assert.Equal(chunks, index.Saved.Chunks.Select(c => c.Chunk));
    }

    [Fact]
    public async Task LaterEmbeddingFailureLeavesExistingIndexUntouchedAndEmptyOutputIsNotDelete()
    {
        var request = new DocumentProcessingRequest(Guid.NewGuid(), Guid.NewGuid(), "text/plain");
        var chunks = Enumerable.Range(0, 2).Select(i => new DocumentChunk(request.DocumentId, request.ManualRevisionId, Guid.NewGuid(), "line", "text")).ToArray();
        var index = new Index(); var embeddings = new Embeddings { Result = call => call == 2 ? throw new InvalidOperationException("failure") : new([new float[] { 1, 0 }], "model-v1") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new ManualIngestionService(new Processor(chunks), embeddings, index, new("profile", "model-v1", 2), 1).IngestAsync(request, Stream.Null, default));
        Assert.Null(index.Saved);
        Assert.Equal(0, await new ManualIngestionService(new Processor([]), embeddings, index, new("profile", "model-v1", 2)).IngestAsync(request, Stream.Null, default));
        Assert.Null(index.Saved);
    }

    [Theory]
    [InlineData("other-model", 2, 1)]
    [InlineData(null, 2, 1)]
    [InlineData("model-v1", 3, 1)]
    [InlineData("model-v1", 2, 0)]
    [InlineData("model-v1", 2, float.NaN)]
    public void RejectsIncompatibleModelsDimensionsAndInvalidCosineVectors(string? model, int dimension, float value)
    {
        var result = new EmbeddingResult([Enumerable.Repeat(value, dimension).ToArray()], model);
        Assert.Throws<InvalidOperationException>(() => new EmbeddingSpace("profile", "model-v1", 2).Validate(result, 1));
    }
}
