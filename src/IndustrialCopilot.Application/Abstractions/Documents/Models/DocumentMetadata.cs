namespace IndustrialCopilot.Application.Abstractions.Documents.Models;

/// <summary>Operator-supplied source description; never interpreted as a filesystem path or fetched URL.</summary>
public sealed record DocumentMetadata
{
    public string Title { get; }
    public string Source { get; }
    public int RevisionNumber { get; }
    public DocumentMetadata(string title, string source, int revisionNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        if (title.Length > 512 || source.Length > 1024) throw new ArgumentException("Source description is too long.");
        if (revisionNumber < 1) throw new ArgumentOutOfRangeException(nameof(revisionNumber));
        Title = title; Source = source; RevisionNumber = revisionNumber;
    }
}
