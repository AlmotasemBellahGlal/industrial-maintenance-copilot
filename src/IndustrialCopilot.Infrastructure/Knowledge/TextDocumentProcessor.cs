using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.Documents;
using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Infrastructure.Knowledge;

/// <summary>Strict UTF-8 text/plain. Locators identify source lines and Unicode scalar offsets,
/// not invented PDF pages. Chunking preserves original text, including line endings.</summary>
public sealed class TextDocumentProcessor : IDocumentProcessor
{
    private readonly int size, overlap, maxCharacters;
    public TextDocumentProcessor(int chunkSize = 1200, int overlap = 200, int maxCharacters = 4_000_000)
    {
        if (chunkSize <= 0 || overlap < 0 || overlap >= chunkSize || maxCharacters < chunkSize)
            throw new ArgumentException("Invalid text chunking limits.");
        size = chunkSize; this.overlap = overlap; this.maxCharacters = maxCharacters;
    }

    public async Task<IReadOnlyList<DocumentChunk>> ProcessAsync(DocumentProcessingRequest request, Stream source, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead) throw new ArgumentException("A readable source is required.", nameof(source));
        if (!MediaTypeHeaderValue.TryParse(request.MediaType, out var media) || !string.Equals(media.MediaType, "text/plain", StringComparison.OrdinalIgnoreCase) ||
            (media.CharSet is { } charset && !charset.Trim('"').Equals("utf-8", StringComparison.OrdinalIgnoreCase)))
            throw new NotSupportedException("Only UTF-8 text/plain manuals are supported; PDF/OCR extraction is external.");
        cancellationToken.ThrowIfCancellationRequested();
        using var reader = new StreamReader(source, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        var text = new StringBuilder();
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) != 0)
            {
                if (text.Length + read > maxCharacters) throw new InvalidOperationException("Manual exceeds the configured text limit.");
                text.Append(buffer, 0, read);
            }
        }
        catch (DecoderFallbackException) { throw new InvalidOperationException("Manual source is not valid UTF-8."); }
        var value = text.ToString();
        if (value.Contains('\0')) throw new InvalidOperationException("Manual source contains invalid text characters.");
        if (value.StartsWith('\uFEFF')) value = value[1..];
        var runes = value.EnumerateRunes().ToArray();
        var lines = new int[runes.Length + 1];
        lines[0] = 1;
        for (var i = 0; i < runes.Length; i++) lines[i + 1] = lines[i] +
            (runes[i].Value == '\n' || (runes[i].Value == '\r' && (i + 1 == runes.Length || runes[i + 1].Value != '\n')) ? 1 : 0);
        var chunks = new List<DocumentChunk>();
        for (var start = 0; start < runes.Length; start += size - overlap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var end = start + Math.Min(size, runes.Length - start);
            var content = string.Concat(runes[start..end].Select(r => r.ToString()));
            if (!string.IsNullOrWhiteSpace(content))
            {
                var locator = FormattableString.Invariant($"text:lines {lines[start]}-{lines[end - 1]}; scalars {start + 1}-{end}");
                var identity = FormattableString.Invariant($"text-v1\n{request.DocumentId:D}\n{request.ManualRevisionId:D}\n{size}\n{overlap}\n{start}\n{content}");
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
                hash[6] = (byte)((hash[6] & 0x0f) | 0x80);
                hash[8] = (byte)((hash[8] & 0x3f) | 0x80);
                chunks.Add(new(request.DocumentId, request.ManualRevisionId, new Guid(hash.AsSpan(0, 16), bigEndian: true), locator, content));
            }
            if (end == runes.Length) break;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return chunks.AsReadOnly();
    }
}
