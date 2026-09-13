using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>Hosted OpenAI transport. Reuse for the host lifetime and dispose at shutdown.
/// Tool calls are returned as data; no tools are executed. There are no retries.</summary>
public sealed class OpenAiLlmProvider : ILlmProvider, IDisposable
{
    private readonly OpenAiOptions options;
    private readonly HttpClient client;

    public OpenAiLlmProvider(OpenAiOptions options)
        : this(options, new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }) { }

    // A transport seam for offline tests. The provider owns and disposes the handler.
    internal OpenAiLlmProvider(OpenAiOptions options, HttpMessageHandler handler)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
        client = new HttpClient(handler, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        var payload = OpenAiProtocol.ChatRequest(options, request, false);
        var result = await PostAsync("v1/chat/completions", payload, OpenAiProtocol.Completion, cancellationToken).ConfigureAwait(false);
        if (result.ToolCalls.Count != 0) throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
        return new(result.Content, result.Usage, result.Model);
    }

    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tools);
        // Snapshot caller-owned tool collections before the first asynchronous boundary.
        var payload = OpenAiProtocol.ChatRequest(options, request, false, tools.ToArray());
        return PostAsync("v1/chat/completions", payload, OpenAiProtocol.Completion, cancellationToken);
    }

    public Task<EmbeddingResult> GenerateEmbeddingsAsync(EmbeddingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Inputs.Count == 0 || request.Inputs.Any(input => input.Length == 0))
            throw new ArgumentException("OpenAI requires nonempty embedding inputs.", nameof(request));
        var inputs = new JsonArray();
        foreach (var input in request.Inputs) inputs.Add(input);
        var payload = new JsonObject { ["model"] = options.EmbeddingModel, ["input"] = inputs, ["encoding_format"] = "float" };
        return PostAsync("v1/embeddings", payload, root => OpenAiProtocol.Embeddings(root, request.Inputs.Count), cancellationToken);
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = OpenAiProtocol.ChatRequest(options, request, true);
        using var deadline = Deadline(options.StreamingTimeout, cancellationToken);
        await using var iterator = ReadStreamAsync(payload, deadline.Token).GetAsyncEnumerator(deadline.Token);
        // Keep normalization outside the iterator containing yield statements. The deadline
        // remains alive for sending, all body reads, and consumer-driven enumeration.
        while (await GuardAsync(() => iterator.MoveNextAsync().AsTask(), cancellationToken, deadline.Token).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return iterator.Current;
        }
    }

    private async Task<T> PostAsync<T>(string path, JsonObject payload, Func<JsonElement, T> map, CancellationToken caller)
    {
        using var deadline = Deadline(options.RequestTimeout, caller);
        return await GuardAsync(async () =>
        {
            using var request = Request(path, payload, false);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            CheckStatus(response.StatusCode);
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: deadline.Token).ConfigureAwait(false);
            var result = Parse(() => map(document.RootElement));
            deadline.Token.ThrowIfCancellationRequested();
            return result;
        }, caller, deadline.Token).ConfigureAwait(false);
    }

    private async IAsyncEnumerable<StreamingChunk> ReadStreamAsync(JsonObject payload,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var request = Request("v1/chat/completions", payload, true);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        CheckStatus(response.StatusCode);
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw OpenAiProtocol.Invalid();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(body, new UTF8Encoding(false, true));
        var data = new StringBuilder();
        TokenUsage? usage = null;
        var finished = false;
        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null) throw OpenAiProtocol.Invalid(); // EOF is not [DONE].
            if (line.Length != 0)
            {
                if (line.StartsWith("data:", StringComparison.Ordinal))
                {
                    var value = line.AsSpan(5);
                    if (value.StartsWith(" ")) value = value[1..];
                    data.Append(value).Append('\n');
                }
                else if (line == "data") data.Append('\n');
                // SSE comments and other fields do not contain completion data.
                continue;
            }
            if (data.Length == 0) continue;
            var eventData = data.ToString(0, data.Length - 1);
            data.Clear();
            if (eventData == "[DONE]")
            {
                if (!finished) throw OpenAiProtocol.Invalid();
                cancellationToken.ThrowIfCancellationRequested();
                yield return new StreamingChunk("", true, usage);
                yield break;
            }
            using var document = JsonDocument.Parse(eventData);
            var chunk = Parse(() =>
            {
                var root = document.RootElement;
                if (root.TryGetProperty("error", out _)) throw OpenAiProtocol.Invalid();
                if (root.TryGetProperty("usage", out var totals) && totals.ValueKind != JsonValueKind.Null)
                {
                    if (usage is not null) throw OpenAiProtocol.Invalid();
                    usage = OpenAiProtocol.Usage(totals);
                }
                var choices = root.GetProperty("choices");
                if (choices.GetArrayLength() == 0)
                {
                    if (!finished || usage is null) throw OpenAiProtocol.Invalid();
                    return null;
                }
                if (finished) throw OpenAiProtocol.Invalid();
                var choice = OpenAiProtocol.SingleChoice(root);
                var delta = choice.GetProperty("delta");
                if (delta.TryGetProperty("role", out var role) && role.GetString() != "assistant") throw OpenAiProtocol.Invalid();
                if ((delta.TryGetProperty("tool_calls", out var calls) && calls.ValueKind != JsonValueKind.Null && calls.GetArrayLength() != 0) ||
                    (delta.TryGetProperty("function_call", out var legacyCall) && legacyCall.ValueKind != JsonValueKind.Null))
                    throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
                if (delta.TryGetProperty("refusal", out var refusal) && refusal.ValueKind != JsonValueKind.Null)
                    throw new LlmProviderException(LlmProviderFailureKind.Refused);
                if (choice.TryGetProperty("finish_reason", out var reason) && reason.ValueKind != JsonValueKind.Null)
                {
                    OpenAiProtocol.CheckFinish(reason.GetString()!);
                    if (reason.GetString() != "stop") throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
                    finished = true;
                }
                var content = delta.TryGetProperty("content", out var text) && text.ValueKind != JsonValueKind.Null ? text.GetString() : null;
                return string.IsNullOrEmpty(content) ? null : new StreamingChunk(content, false);
            });
            if (chunk is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return chunk;
            }
        }
    }

    private HttpRequestMessage Request(string path, JsonObject payload, bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(options.Endpoint, path));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(stream ? "text/event-stream" : "application/json"));
        request.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        return request;
    }

    private static CancellationTokenSource Deadline(TimeSpan timeout, CancellationToken caller)
    {
        caller.ThrowIfCancellationRequested();
        var source = CancellationTokenSource.CreateLinkedTokenSource(caller);
        source.CancelAfter(timeout);
        return source;
    }

    private static async Task<T> GuardAsync<T>(Func<Task<T>> operation, CancellationToken caller, CancellationToken deadline)
    {
        try { return await operation().ConfigureAwait(false); }
        catch (OperationCanceledException) when (caller.IsCancellationRequested) { throw new OperationCanceledException(caller); }
        catch (Exception error) when (caller.IsCancellationRequested && (error is HttpRequestException or IOException))
        { throw new OperationCanceledException(caller); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { throw new LlmProviderException(LlmProviderFailureKind.Timeout); }
        catch (OperationCanceledException) { throw new LlmProviderException(LlmProviderFailureKind.Transport); }
        catch (HttpRequestException) { throw new LlmProviderException(LlmProviderFailureKind.Transport); }
        catch (IOException) { throw new LlmProviderException(LlmProviderFailureKind.Transport); }
        catch (JsonException) { throw OpenAiProtocol.Invalid(); }
        catch (DecoderFallbackException) { throw OpenAiProtocol.Invalid(); }
    }

    private static T Parse<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw OpenAiProtocol.Invalid(); }
    }

    private static void CheckStatus(HttpStatusCode status)
    {
        if ((int)status is >= 200 and <= 299) return;
        throw new LlmProviderException((int)status switch
        {
            401 or 403 => LlmProviderFailureKind.Authentication,
            429 => LlmProviderFailureKind.RateLimited,
            408 => LlmProviderFailureKind.Timeout,
            400 or 404 or 422 => LlmProviderFailureKind.InvalidRequest,
            500 or 502 or 503 or 504 => LlmProviderFailureKind.Unavailable,
            _ => LlmProviderFailureKind.HttpFailure
        });
    }

    public void Dispose() => client.Dispose();
}
