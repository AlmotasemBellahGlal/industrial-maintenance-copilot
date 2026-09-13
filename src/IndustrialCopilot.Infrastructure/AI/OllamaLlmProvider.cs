using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>Compatible chat and native embeddings. Requires installed models supporting
/// the requested capabilities. No tools are executed and no embeddings are truncated.</summary>
public sealed class OllamaLlmProvider : ILlmProvider, IDisposable
{
    private readonly OllamaOptions options;
    private readonly ChatCompletionsClient client;

    public OllamaLlmProvider(OllamaOptions options) : this(options, null) { }

    internal OllamaLlmProvider(OllamaOptions options, HttpMessageHandler? handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        client = new(options.Endpoint, options.ChatModel, null, options.RequestTimeout, options.StreamingTimeout, true, handler);
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
            throw new ArgumentException("Nonempty embedding inputs are required.", nameof(request));
        var inputs = new JsonArray();
        foreach (var input in request.Inputs) inputs.Add(input);
        var payload = new JsonObject { ["model"] = options.EmbeddingModel, ["input"] = inputs, ["truncate"] = false };
        return client.PostAsync("api/embed", payload, root => MapEmbeddings(root, request.Inputs.Count), cancellationToken);
    }

    private static EmbeddingResult MapEmbeddings(JsonElement root, int count)
    {
        var data = root.GetProperty("embeddings");
        if (data.GetArrayLength() != count) throw OpenAiProtocol.Invalid();
        var vectors = new List<IReadOnlyList<float>>();
        var dimensions = 0;
        // Native /api/embed returns vectors in input order, with no index field.
        foreach (var item in data.EnumerateArray())
        {
            var vector = item.EnumerateArray().Select(value => value.GetSingle()).ToArray();
            if (vector.Length == 0 || vector.Any(value => !float.IsFinite(value)) || (dimensions != 0 && dimensions != vector.Length))
                throw OpenAiProtocol.Invalid();
            dimensions = vector.Length;
            vectors.Add(vector);
        }
        return new(vectors, OpenAiProtocol.RequiredText(root, "model"));
    }

    public void Dispose() => client.Dispose();
}
