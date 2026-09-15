using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;
namespace IndustrialCopilot.Infrastructure.Knowledge;

public sealed class DeterministicDocumentChunker : IDocumentChunker
{
    private readonly int size, overlap;
    public DeterministicDocumentChunker(int chunkSize = 1200, int overlap = 200)
    {
        if (chunkSize <= 0 || overlap < 0 || overlap >= chunkSize) throw new ArgumentException("Invalid chunk limits.");
        size = chunkSize; this.overlap = overlap;
    }
    private static string? Section(string text)
    {
        var headings = text.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("Section ", StringComparison.Ordinal) && l.Contains(':')).ToArray();
        return headings.Length == 1 ? headings[0] : null;
    }
    public IReadOnlyList<DocumentChunk> Chunk(DocumentProcessingRequest request, ExtractedDocument document, CancellationToken cancellationToken)
    {
        var chunks = new List<DocumentChunk>();
        foreach (var segment in document.Segments)
        {
            var value = segment.Text;
            var section = Section(value);
            var identityVersion = segment.Page is {} number ? FormattableString.Invariant($"pdf-v1-page-{number}") : "text-v1";
            var runes = value.EnumerateRunes().ToArray();
            var lines = new int[runes.Length + 1];
            lines[0] = 1;
            for (var i = 0; i < runes.Length; i++) lines[i + 1] = lines[i] +
                (runes[i].Value == '\n' || (runes[i].Value == '\r' && (i + 1 == runes.Length || runes[i + 1].Value != '\n')) ? 1 : 0);

            for (var start = 0; start < runes.Length; start += size - overlap)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var end = start + Math.Min(size, runes.Length - start);
                var content = string.Concat(runes[start..end].Select(r => r.ToString()));
                if (!string.IsNullOrWhiteSpace(content))
                {
                    var locator = FormattableString.Invariant($"text:lines {lines[start]}-{lines[end - 1]}; scalars {start + 1}-{end}");
                    var identity = FormattableString.Invariant($"{identityVersion}\n{request.DocumentId:D}\n{request.ManualRevisionId:D}\n{size}\n{overlap}\n{start}\n{content}");
                    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
                    hash[6] = (byte)((hash[6] & 0x0f) | 0x80);
                    hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
                    if (segment.Page is {} page) locator = FormattableString.Invariant($"pdf:page {page}; ") + locator;
                    chunks.Add(new(request.DocumentId, request.ManualRevisionId, new Guid(hash.AsSpan(0, 16), bigEndian: true), locator, content,
                        request.Metadata, segment.Page, section));
                }
                if (end == runes.Length) break;
            }

        }
        return chunks.AsReadOnly();
    }
}
