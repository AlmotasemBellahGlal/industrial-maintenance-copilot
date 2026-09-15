using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using IndustrialCopilot.Infrastructure.Knowledge;

namespace IndustrialCopilot.Infrastructure.Tests.Knowledge;

public class TextProcessingTests
{
    private static DocumentProcessingRequest Request => new(Guid.Parse("00000000-0000-0000-0000-000000000001"), Guid.Parse("00000000-0000-0000-0000-000000000002"), "text/plain");

    [Fact]
    public async Task DeterministicWindowsOverlapAndIdentityPreserveOriginalProvenance()
    {
        var processor = new TextDocumentProcessor(6, 2);
        using var first = new MemoryStream(Encoding.UTF8.GetBytes("abcdefghij"));
        using var second = new MemoryStream(Encoding.UTF8.GetBytes("abcdefghij"));
        var chunks = await processor.ProcessAsync(Request, first, default);
        var again = await processor.ProcessAsync(Request, second, default);
        Assert.Equal(new[] { "abcdef", "efghij" }, chunks.Select(c => c.Content));
        Assert.Equal(chunks.Select(c => c.ChunkId), again.Select(c => c.ChunkId));
        Assert.Equal(2, chunks.Select(c => c.ChunkId).Distinct().Count());
        Assert.All(chunks, c => { Assert.Equal(Request.DocumentId, c.DocumentId); Assert.Equal(Request.ManualRevisionId, c.ManualRevisionId); });
        Assert.Equal("text:lines 1-1; scalars 5-10", chunks[1].Locator);
        Assert.True(first.CanRead);
        using var changed = new MemoryStream(Encoding.UTF8.GetBytes("abcdefghij"));
        var other = await processor.ProcessAsync(new(Request.DocumentId, Guid.NewGuid(), "text/plain"), changed, default);
        Assert.NotEqual(chunks[0].ChunkId, other[0].ChunkId);
    }

    [Fact]
    public async Task UnicodeScalarsAndLineLocatorsSurviveBoundariesAndCurrentPosition()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes("skip😀\r\nAB\rCD"));
        source.Position = 4;
        var chunks = await new TextDocumentProcessor(4, 1).ProcessAsync(Request, source, default);
        Assert.Equal("😀\r\nA", chunks[0].Content);
        Assert.Equal("text:lines 1-2; scalars 1-4", chunks[0].Locator);
        Assert.DoesNotContain(chunks, c => c.Content.Contains('\uFFFD'));
        Assert.Contains(chunks, c => c.Locator.Contains("3"));
    }

    [Fact]
    public async Task DoesNotRequireSeekingAndDoesNotOwnTheSource()
    {
        using var source = new NonSeekable(Encoding.UTF8.GetBytes("manual"));
        Assert.Single(await new TextDocumentProcessor().ProcessAsync(Request, source, default));
        Assert.True(source.CanRead);
    }

    [Fact]
    public async Task EmptyTextProducesNoReplacementChunks()
    {
        using var source = new MemoryStream(Encoding.UTF8.GetBytes(" \n\t"));
        Assert.Empty(await new TextDocumentProcessor().ProcessAsync(Request, source, default));
    }

    [Fact]
    public async Task RejectsUnsupportedCorruptOversizedAndCancelledInput()
    {
        var processor = new TextDocumentProcessor(2, 0, 4);
        using var source = new MemoryStream([255]);
        await Assert.ThrowsAsync<NotSupportedException>(() => processor.ProcessAsync(new(Request.DocumentId, Request.ManualRevisionId, "application/pdf"), source, default));
        await Assert.ThrowsAsync<IndustrialCopilot.Application.Abstractions.Documents.DocumentInputException>(() => processor.ProcessAsync(Request, source, default));
        using var oversized = new MemoryStream(Encoding.UTF8.GetBytes("abcdef"));
        await Assert.ThrowsAsync<IndustrialCopilot.Application.Abstractions.Documents.DocumentInputException>(() => processor.ProcessAsync(Request, oversized, default));
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processor.ProcessAsync(Request, oversized, cancelled.Token));
        Assert.Throws<ArgumentException>(() => new TextDocumentProcessor(10, 10));
    }

    private sealed class NonSeekable(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
