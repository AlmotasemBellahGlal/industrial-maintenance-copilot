using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.AI.Models;
using IndustrialCopilot.Application.Abstractions.Retrieval;
using IndustrialCopilot.Application.Abstractions.Retrieval.Models;

namespace IndustrialCopilot.Application.Actions;

/// <summary>Host binds authenticated execution context. No ambient permissive fallback.</summary>
public interface ITrustedToolContextAccessor { TrustedToolContext? Current { get; } }

/// <summary>Routes existing agent retrieval through the same trusted tool boundary.</summary>
public sealed class TrustedRetrievalService(TrustedToolExecutor executor,ITrustedToolContextAccessor context) : IRetrievalService
{
    public async Task<IReadOnlyList<RetrievalResult>> RetrieveAsync(RetrievalQuery query,RetrievalMode mode,CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        if(query.DocumentId is null || query.ManualRevisionId is null || !Enum.IsDefined(mode)) throw new ArgumentException("Scoped retrieval required.");
        var arguments=JsonSerializer.SerializeToElement(new {query=query.QueryText,topK=query.TopK,documentId=query.DocumentId,manualRevisionId=query.ManualRevisionId,mode=mode.ToString()});
        var result=await executor.ExecuteAsync(new ToolCall(Guid.NewGuid().ToString("D"),"retrieve_manual_evidence",arguments),context.Current,ct);
        return result.Outcome==ToolExecutionOutcome.Completed && result.Evidence is not null ? result.Evidence
            : throw new InvalidOperationException("Trusted retrieval unavailable.");
    }
}
