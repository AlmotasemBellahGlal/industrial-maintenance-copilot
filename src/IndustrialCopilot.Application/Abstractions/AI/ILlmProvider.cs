using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Application.Abstractions.AI;

public interface ILlmProvider
{
    Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken);

    IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken cancellationToken);

    Task<ToolCompletionResponse> CompleteWithToolsAsync(
        CompletionRequest request,
        IReadOnlyList<ToolDefinition> tools,
        CancellationToken cancellationToken);

    Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken);
}
