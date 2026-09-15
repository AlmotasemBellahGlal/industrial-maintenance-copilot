using IndustrialCopilot.Application.Abstractions.Documents;
namespace IndustrialCopilot.Infrastructure.Knowledge;

/// <summary>Conservative cleaning preserves line/scalar citation offsets: remove only a leading BOM,
/// reject invalid controls. Do not collapse whitespace, dehyphenate, or invent headings.</summary>
public sealed class DocumentCleaner : IDocumentCleaner
{
    public ExtractedDocument Clean(ExtractedDocument document, CancellationToken cancellationToken)
    {
        var result = new List<ExtractedSegment>();
        foreach (var segment in document.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = segment.Text;
            if (text.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')))
                throw new DocumentInputException(IngestionFailure.InvalidDocument);
            result.Add(new(text.StartsWith('\uFEFF') ? text[1..] : text, segment.Page));
        }
        return new(result.AsReadOnly(), document.PageCount);
    }
}
