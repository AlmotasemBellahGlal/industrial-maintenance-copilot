using System.Net.Http.Headers;
using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
namespace IndustrialCopilot.Infrastructure.Knowledge;

internal static class BoundedDocumentSource
{
    internal static async Task<byte[]> ReadAsync(Stream source, int limit, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A readable source is required.");
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await source.ReadAsync(bytes, token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > limit) throw new DocumentInputException(IngestionFailure.InputTooLarge);
            buffer.Write(bytes, 0, count);
        }
        token.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }
}
public sealed class Utf8DocumentExtractor(int maxCharacters = 4_000_000) : IDocumentExtractor
{
    public async Task<ExtractedDocument> ExtractAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.MediaType, out var media) || !string.Equals(media.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase) ||
            (media.CharSet is {} charset && !charset.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Only UTF-8 text/plain is supported by this extractor.");
        var bytes = await BoundedDocumentSource.ReadAsync(source, Math.Min(16_000_000, checked(maxCharacters * 4)), cancellationToken).ConfigureAwait(false);
        string text;
        try { text = new UTF8Encoding(false, true).GetString(bytes); }
        catch (DecoderFallbackException) { throw new DocumentInputException(IngestionFailure.InvalidDocument); }
        if (text.Length > maxCharacters) throw new DocumentInputException(IngestionFailure.InputTooLarge);
        return new(Array.AsReadOnly(new[] { new ExtractedSegment(text) }), null);
    }
}
/// <summary>Selectable-text PDFs only. No OCR, execution, URL loading or attachment extraction.</summary>
public sealed class PdfDocumentExtractor(int maxBytes = 16_000_000, int maxPages = 500, int maxCharacters = 4_000_000) : IDocumentExtractor
{
    public async Task<ExtractedDocument> ExtractAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        if (!MediaTypeHeaderValue.TryParse(request.MediaType, out var media) || !string.Equals(media.MediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("Only application/pdf is supported by this extractor.");
        var bytes = await BoundedDocumentSource.ReadAsync(source, maxBytes, cancellationToken).ConfigureAwait(false);
        try
        {
            using var pdf = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = false });
            if (pdf.NumberOfPages < 1 || pdf.NumberOfPages > maxPages) throw new DocumentInputException(IngestionFailure.InputTooLarge);
            var segments = new List<ExtractedSegment>();
            var count = 0;
            foreach (var page in pdf.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var text = ContentOrderTextExtractor.GetText(page);
                count += text.Length;
                if (count > maxCharacters) throw new DocumentInputException(IngestionFailure.InputTooLarge);
                // Fail closed on image-only/blank pages rather than silently publishing partial manuals.
                if (string.IsNullOrWhiteSpace(text)) throw new DocumentInputException(IngestionFailure.InvalidDocument);
                segments.Add(new(text, page.Number));
            }
            return new(segments.AsReadOnly(), pdf.NumberOfPages);
        }
        catch (OperationCanceledException) { throw; }
        catch (DocumentInputException) { throw; }
        catch (Exception) { throw new DocumentInputException(IngestionFailure.InvalidDocument); }
    }
}
public sealed class ManualDocumentExtractor : IDocumentExtractor
{
    public async Task<ExtractedDocument> ExtractAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!MediaTypeHeaderValue.TryParse(request.MediaType, out var media)) throw new DocumentInputException(IngestionFailure.UnsupportedFormat);
        IDocumentExtractor extractor = media.MediaType?.ToLowerInvariant() switch
        {
            "text/plain" => new Utf8DocumentExtractor(),
            "application/pdf" => new PdfDocumentExtractor(),
            _ => throw new DocumentInputException(IngestionFailure.UnsupportedFormat)
        };
        try { return await extractor.ExtractAsync(request, source, cancellationToken).ConfigureAwait(false); }
        catch (NotSupportedException) { throw new DocumentInputException(IngestionFailure.UnsupportedFormat); }
    }
}
