using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Abstractions.Documents;

/// <summary>A source segment is a whole text file or one actual PDF page. Text is never paginated artificially.</summary>
public sealed record ExtractedSegment
{
    public string Text { get; }
    public int? Page { get; }
    public ExtractedSegment(string text, int? page = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (page is <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        Text = text; Page = page;
    }
}
public sealed record ExtractedDocument
{
    public IReadOnlyList<ExtractedSegment> Segments { get; }
    public int? PageCount { get; }
    public ExtractedDocument(IReadOnlyList<ExtractedSegment> segments, int? pageCount)
    {
        ArgumentNullException.ThrowIfNull(segments);
        var copy = segments.ToArray();
        if (copy.Any(s => s is null)) throw new ArgumentException("Null source segment.");
        if (pageCount is < 1 || (pageCount is null && copy.Any(s => s.Page is not null)) ||
            (pageCount is {} pages && (copy.Length != pages || !copy.Select(s => s.Page).SequenceEqual(Enumerable.Range(1, pages).Select(p => (int?)p)))))
            throw new ArgumentException("Page provenance must match the actual extracted pages.");
        Segments = Array.AsReadOnly(copy); PageCount = pageCount;
    }
}
public interface IDocumentExtractor
{
    Task<ExtractedDocument> ExtractAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken);
}
public interface IDocumentCleaner
{
    ExtractedDocument Clean(ExtractedDocument document, CancellationToken cancellationToken);
}
public interface IDocumentChunker
{
    IReadOnlyList<DocumentChunk> Chunk(DocumentProcessingRequest request, ExtractedDocument document, CancellationToken cancellationToken);
}
