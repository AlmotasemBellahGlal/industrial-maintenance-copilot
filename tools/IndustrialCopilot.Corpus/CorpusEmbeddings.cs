using System.Security.Cryptography;
using System.Text;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;
namespace IndustrialCopilot.Corpus;

/// <summary>Deterministic lexical test vectors, never a production semantic embedding model.
/// Uses 256 hash buckets (SHA256 bytes 0+1 mod 256) to reduce the collision rate that
/// caused near-identical similarity between unrelated equipment families at 32 dimensions.</summary>
public sealed class CorpusEmbeddings : ILlmProvider
{
    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        var result = new List<IReadOnlyList<float>>();
        foreach (var input in request.Inputs)
        {
            cancellationToken.ThrowIfCancellationRequested(); var vector = new float[256]; vector[0] = .01f;
            foreach (var word in input.ToLowerInvariant().Split([' ', '\n', '\r', '.', ',', ':', ';'], StringSplitOptions.RemoveEmptyEntries))
            {
                var h = SHA256.HashData(Encoding.UTF8.GetBytes(word));
                // Use two SHA256 bytes combined to address 256 buckets, reducing collisions 8× vs 32 dims.
                vector[((h[0] << 8) | h[1]) % 256] += 1;
            }
            result.Add(vector);
        }
        return Task.FromResult(new EmbeddingResult(result, "synthetic-lexical-v1"));
    }
    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request, IReadOnlyList<ToolDefinition> tools, CancellationToken token) => throw new NotSupportedException();
    public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken token) => throw new NotSupportedException();
}
