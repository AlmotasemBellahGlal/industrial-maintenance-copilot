using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Application.Abstractions.Indexing.Models;

namespace IndustrialCopilot.Application.Tests.Abstractions.Indexing;

public class IndexingContractTests
{
    private static DocumentChunk Chunk(Guid document, Guid revision, Guid? chunkId = null) =>
        new(document, revision, chunkId ?? Guid.NewGuid(), "Page 3", "Inspect coupling");

    [Fact]
    public void IndexedChunkRequiresContentAndNonemptyVector()
    {
        var chunk = Chunk(Guid.NewGuid(), Guid.NewGuid());
        Assert.Throws<ArgumentNullException>(() => new IndexedChunk(null!, [1]));
        Assert.Throws<ArgumentNullException>(() => new IndexedChunk(chunk, null!));
        Assert.Throws<ArgumentException>(() => new IndexedChunk(chunk, []));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void RejectsNonfiniteVectorElements(float value) =>
        Assert.Throws<ArgumentException>(() => new IndexedChunk(Chunk(Guid.NewGuid(), Guid.NewGuid()), [1, value]));

    [Fact]
    public void RejectsEmptyReplacementIdentities()
    {
        var id = Guid.NewGuid();
        var chunks = new[] { new IndexedChunk(Chunk(id, id), [1]) };
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(Guid.Empty, id, "profile-v1", chunks));
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(id, Guid.Empty, "profile-v1", chunks));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingEmbeddingProfile(string? profile)
    {
        var id = Guid.NewGuid();
        Assert.ThrowsAny<ArgumentException>(() => new RevisionIndexRequest(id, id, profile!, [new IndexedChunk(Chunk(id, id), [1])]));
    }

    [Fact]
    public void RejectsNullEmptyOrNullContainingBatch()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentNullException>(() => new RevisionIndexRequest(id, id, "profile-v1", null!));
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(id, id, "profile-v1", []));
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(id, id, "profile-v1", [new IndexedChunk(Chunk(id, id), [1]), null!]));
    }

    [Fact]
    public void RejectsDuplicateChunkIdentityEvenForDifferentContent()
    {
        var id = Guid.NewGuid();
        var chunk = Chunk(id, id);
        var duplicate = new DocumentChunk(id, id, chunk.ChunkId, "Page 4", "Different content");
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(id, id, "profile-v1",
            [new IndexedChunk(chunk, [1]), new IndexedChunk(duplicate, [2])]));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RejectsChunkFromAnotherDocumentOrRevision(bool differentDocument)
    {
        var document = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var wrong = Chunk(differentDocument ? Guid.NewGuid() : document, differentDocument ? revision : Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(document, revision, "profile-v1",
            [new IndexedChunk(Chunk(document, revision), [1]), new IndexedChunk(wrong, [2])]));
    }

    [Fact]
    public void RejectsInconsistentVectorDimensions()
    {
        var id = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new RevisionIndexRequest(id, id, "profile-v1",
            [new IndexedChunk(Chunk(id, id), [1]), new IndexedChunk(Chunk(id, id), [1, 2])]));
    }

    [Fact]
    public void ValidBatchOwnsCollectionsAndVectorsWithoutProviderModelIdentifier()
    {
        var document = Guid.NewGuid();
        var revision = Guid.NewGuid();
        var array = new float[] { -2, 0 };
        var list = new List<float> { 3, 4 };
        var first = new IndexedChunk(Chunk(document, revision), array);
        var second = new IndexedChunk(Chunk(document, revision), list);
        var batch = new List<IndexedChunk> { first, second };
        var request = new RevisionIndexRequest(document, revision, "maintenance-embeddings-v1", batch);
        array[0] = float.NaN;
        list.Clear();
        batch.Clear();
        Assert.Equal(2, request.Chunks.Count);
        Assert.Equal(new float[] { -2, 0 }, request.Chunks[0].Vector);
        Assert.Equal(new float[] { 3, 4 }, request.Chunks[1].Vector);
        Assert.Throws<NotSupportedException>(() => ((IList<IndexedChunk>)request.Chunks).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<float>)first.Vector)[0] = 99);
        Assert.Throws<NotSupportedException>(() => ((IList<float>)second.Vector).Clear());
    }
}
