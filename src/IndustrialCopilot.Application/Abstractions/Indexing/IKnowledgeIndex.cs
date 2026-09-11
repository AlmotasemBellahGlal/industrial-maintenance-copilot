using IndustrialCopilot.Application.Abstractions.Indexing.Models;

namespace IndustrialCopilot.Application.Abstractions.Indexing;

public interface IKnowledgeIndex
{
    /// <summary>Publishes the complete searchable text, provenance, and vectors of one revision.</summary>
    /// <remarks>
    /// Request must be non-null. Replacement scope is exactly (DocumentId, ManualRevisionId).
    /// Equivalent retries must not append duplicates; obsolete chunks in that revision are replaced.
    /// Other revisions remain untouched. Readers must not observe a partially replaced revision.
    /// Concurrent replacements must not produce a union of batches.
    /// Retrying the same replacement after uncertain cancellation must be safe.
    /// Empty replacement is not a delete operation and is rejected by the request contract.
    /// EmbeddingProfile identifies a configured compatible embedding space, independently of provider names.
    /// </remarks>
    Task ReplaceRevisionAsync(RevisionIndexRequest request, CancellationToken cancellationToken);
}
