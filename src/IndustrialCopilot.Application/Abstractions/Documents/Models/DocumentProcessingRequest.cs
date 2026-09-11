namespace IndustrialCopilot.Application.Abstractions.Documents.Models;

public sealed record DocumentProcessingRequest
{
    // DocumentId corresponds to Domain Manual.Id.
    public Guid DocumentId { get; }
    public Guid ManualRevisionId { get; }
    public string MediaType { get; }

    public DocumentProcessingRequest(Guid documentId, Guid manualRevisionId, string mediaType)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity is required.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity is required.", nameof(manualRevisionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
        MediaType = mediaType;
    }
}
