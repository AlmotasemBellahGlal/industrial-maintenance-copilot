namespace IndustrialCopilot.Application.Abstractions.Retrieval.Models;

public sealed record RetrievalQuery
{
    public string QueryText { get; }
    public int TopK { get; }
    // DocumentId corresponds to Domain Manual.Id. Null means no document filter.
    public Guid? DocumentId { get; }
    public Guid? ManualRevisionId { get; }

    public RetrievalQuery(string queryText, int topK, Guid? documentId = null, Guid? manualRevisionId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        if (topK <= 0) throw new ArgumentOutOfRangeException(nameof(topK));
        if (documentId == Guid.Empty) throw new ArgumentException("Document identity cannot be empty.", nameof(documentId));
        if (manualRevisionId == Guid.Empty) throw new ArgumentException("Revision identity cannot be empty.", nameof(manualRevisionId));
        QueryText = queryText;
        TopK = topK;
        DocumentId = documentId;
        ManualRevisionId = manualRevisionId;
    }
}
