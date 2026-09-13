using System.Runtime.CompilerServices;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>One primary attempt, optionally one fallback. No tools are executed.
/// Dependencies are owned by the host. Embeddings never fall back.</summary>
public sealed class ConfiguredLlmProvider : ILlmProvider
{
    private readonly ILlmProvider primary;
    private readonly ILlmProvider? fallback;
    private readonly ILlmProvider embeddings;

    internal ConfiguredLlmProvider(ILlmProvider primary, ILlmProvider? fallback, ILlmProvider embeddings)
    {
        this.primary = primary;
        this.fallback = fallback;
        this.embeddings = embeddings;
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(provider => provider.CompleteAsync(request, cancellationToken), cancellationToken);

    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tools);
        // Both attempts must see the exact same tool definitions, despite caller mutation.
        var snapshot = Array.AsReadOnly(tools.ToArray());
        return ExecuteAsync(provider => provider.CompleteWithToolsAsync(request, snapshot, cancellationToken), cancellationToken);
    }

    private async Task<T> ExecuteAsync<T>(Func<ILlmProvider, Task<T>> execute, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return await execute(primary).ConfigureAwait(false); }
        catch (LlmProviderException error) when (fallback is not null && ProviderFailures.CanFallback(error))
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await execute(fallback).ConfigureAwait(false);
        }
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var emitted = false;
        var useFallback = false;
        // Acquire/advance inside the catch boundary; yield stays outside it.
        IAsyncEnumerator<StreamingChunk>? iterator = null;
        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    iterator ??= primary.StreamAsync(request, cancellationToken).GetAsyncEnumerator(cancellationToken);
                    hasNext = await iterator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (LlmProviderException error) when (!emitted && fallback is not null && ProviderFailures.CanFallback(error))
                {
                    useFallback = true;
                    break;
                }
                if (!hasNext) break;
                cancellationToken.ThrowIfCancellationRequested();
                emitted = true;
                yield return iterator.Current;
            }
        }
        finally
        {
            if (iterator is not null) await iterator.DisposeAsync().ConfigureAwait(false);
        }
        if (useFallback)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var chunk in fallback!.StreamAsync(request, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }

    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return embeddings.GenerateEmbeddingsAsync(request, cancellationToken);
    }
}
