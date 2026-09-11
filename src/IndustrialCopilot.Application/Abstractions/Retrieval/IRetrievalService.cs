using IndustrialCopilot.Application.Abstractions.Retrieval.Models;

namespace IndustrialCopilot.Application.Abstractions.Retrieval;

public interface IRetrievalService
{
    // Returns a non-null, read-only snapshot with no null elements, at most TopK results,
    // ordered by descending relevance. Supplied filters are combined with AND.
    // No matches yields an empty collection. Implementations reject undefined modes.
    // Hybrid fusion and conversion from provider scores remain implementation concerns.
    Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(
        RetrievalQuery query,
        RetrievalMode mode,
        CancellationToken cancellationToken);
}
