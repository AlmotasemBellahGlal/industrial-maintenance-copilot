using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>Hosted OpenAI adapter. Reuse for the host lifetime and dispose at shutdown.
/// Tool calls are data only; this adapter never executes them.</summary>
public sealed class OpenAiLlmProvider : ILlmProvider, IDisposable
{
    private readonly OpenAiOptions options;
    private readonly ChatCompletionsClient client;

    public OpenAiLlmProvider(OpenAiOptions options) : this(options, null) { }

    internal OpenAiLlmProvider(OpenAiOptions options, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        client = new(options.Endpoint, options.ChatModel, options.ApiKey,
            options.RequestTimeout, options.StreamingTimeout, false, handler);
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken) =>
        client.CompleteAsync(request, cancellationToken);

    public IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request, CancellationToken cancellationToken) =>
        client.StreamAsync(request, cancellationToken);

    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken) =>
        client.CompleteWithToolsAsync(request, tools, cancellationToken);

    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Inputs.Count == 0 || request.Inputs.Any(input => input.Length == 0))
            throw new ArgumentException("OpenAI requires nonempty embedding inputs.", nameof(request));
        var inputs = new JsonArray();
        foreach (var input in request.Inputs) inputs.Add(input);
        var payload = new JsonObject { ["model"] = options.EmbeddingModel, ["input"] = inputs, ["encoding_format"] = "float" };
        return client.PostAsync("v1/embeddings", payload, root => OpenAiProtocol.Embeddings(root, request.Inputs.Count), cancellationToken);
    }

    public void Dispose() => client.Dispose();
}
