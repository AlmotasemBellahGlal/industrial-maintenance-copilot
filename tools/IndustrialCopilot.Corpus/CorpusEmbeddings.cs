using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
namespace IndustrialCopilot.Corpus;

/// <summary>Deterministic lexical test vectors, never a production semantic embedding model.</summary>
public sealed class CorpusEmbeddings : ILlmProvider
{
    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        var result = new List<IReadOnlyList<float>>();
        foreach (var input in request.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested(); var vector = new float[32]; vector[0] = .01f;
            foreach (var word in input.ToLowerInvariant().Split([' ', '\n', '\r', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
                vector[SHA256.HashData(Encoding.UTF8.GetBytes(word))[0] % 32] += 1;
            result.Add(vector);
        }
        return Task.FromResult(new EmbeddingResult(result, "synthetic-lexical-v1"));
    }
    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken token) => throw new NotSupportedException();
    public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
}
