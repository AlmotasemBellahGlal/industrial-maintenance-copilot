using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IndustrialCopilot.Application.Abstractions.AI.Models;

namespace IndustrialCopilot.Infrastructure.AI;

// Shared only for the compatible chat protocol. Embedding mappings stay in adapters.
internal sealed class ChatCompletionsClient : IDisposable
{
    private readonly Uri endpoint;
    private readonly string chatModel;
    private readonly string? apiKey;
    private readonly TimeSpan requestTimeout;
    private readonly TimeSpan streamingTimeout;
    private readonly bool ollama;
    private readonly HttpClient client;

    internal ChatCompletionsClient(Uri endpoint, string chatModel, string? apiKey,
        TimeSpan requestTimeout, TimeSpan streamingTimeout, bool ollama, HttpMessageHandler? handler)
    {
        this.endpoint = endpoint;
        this.chatModel = chatModel;
        this.apiKey = apiKey;
        this.requestTimeout = requestTimeout;
        this.streamingTimeout = streamingTimeout;
        this.ollama = ollama;
        client = new HttpClient(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5)
        }, disposeHandler: true) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private void ValidatePayload(JsonObject payload)
    {
        if (payload.ToJsonString().Length > 1048576) throw new ArgumentException("Provider input budget exceeded.");
        if (payload["messages"] is JsonArray messages)
        {
            if (messages.Count > 128) throw new ArgumentException("Provider message budget exceeded.");
            var property = ollama ? "max_tokens" : "max_completion_tokens";
            var maximum = payload[property]?.GetValue<int>() ?? 4096;
            if (maximum > 16384) throw new ArgumentException("Provider output budget exceeded.");
            payload[property] = maximum;
        }
        if (!ollama) HostedDataBoundary.Apply(payload, apiKey);
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken cancellationToken)
    {
        var payload = OpenAiProtocol.ChatRequest(chatModel, request, false, ollama);
        var result = await PostAsync("v1/chat/completions", payload, OpenAiProtocol.Completion, cancellationToken).ConfigureAwait(false);
        if (result.ToolCalls.Count != 0) throw new LlmProviderException(LlmProviderFailureKind.UnsupportedResponse);
        return new(result.Content, result.Usage, result.Model);
    }

    public Task<ToolCompletionResponse> CompleteWithToolsAsync(CompletionRequest request,
        IReadOnlyList<ToolDefinition> tools, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tools);
        // Snapshot caller-owned tool collections before the first asynchronous boundary.
        var payload = OpenAiProtocol.ChatRequest(chatModel, request, false, ollama, tools.ToArray());
        return PostAsync("v1/chat/completions", payload, OpenAiProtocol.Completion, cancellationToken);
    }

    public async IAsyncEnumerable<StreamingChunk> StreamAsync(CompletionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var payload = OpenAiProtocol.ChatRequest(chatModel, request, true, ollama);
        ValidatePayload(payload);
        using var deadline = Deadline(streamingTimeout, cancellationToken);
        await using var iterator = ReadStreamAsync(payload, deadline.Token).GetAsyncEnumerator(deadline.Token);
        // Keep normalization outside the iterator containing yield statements. The deadline
        // remains alive for sending, all body reads, and consumer-driven enumeration.
        while (await GuardAsync(() => iterator.MoveNextAsync().AsTask(), cancellationToken, deadline.Token).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return iterator.Current;
        }
    }

    internal async Task<T> PostAsync<T>(string path, JsonObject payload, Func<JsonElement, T> map, CancellationToken caller)
    {
        ValidatePayload(payload);
        using var deadline = Deadline(requestTimeout, caller);
        return await GuardAsync(async () =>
        {
            using var request = Request(path, payload, false);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            await ProviderFailures.CheckStatusAsync(response, deadline.Token).ConfigureAwait(false);
            await using var body = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            using var bounded = new MemoryStream();
            var buffer = new byte[8192];
            int read;
            while ((read = await body.ReadAsync(buffer, deadline.Token).ConfigureAwait(false)) != 0)
            {
                if (bounded.Length + read > 4 * 1024 * 1024) throw OpenAiProtocol.Invalid();
                bounded.Write(buffer, 0, read);
            }
            bounded.Position = 0;
            using var document = await JsonDocument.ParseAsync(bounded, cancellationToken: deadline.Token).ConfigureAwait(false);
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
        await ProviderFailures.CheckStatusAsync(response, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(response.Content.Headers.ContentType?.MediaType, "text/event-stream", StringComparison.OrdinalIgnoreCase))
            throw OpenAiProtocol.Invalid();
        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        TokenUsage? usage = null;
        var finished = false;
        await foreach (var eventData in SseReader.ReadAsync(body, cancellationToken).ConfigureAwait(false))
        {
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
        throw OpenAiProtocol.Invalid(); // EOF is not protocol completion.
    }

    private HttpRequestMessage Request(string path, JsonObject payload, bool stream)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, path));
        if (apiKey is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
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
        catch (Exception error) when (caller.IsCancellationRequested && (error is HttpRequestException or IOException or LlmProviderException))
        { throw new OperationCanceledException(caller); }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { throw new LlmProviderException(LlmProviderFailureKind.Timeout); }
        catch (Exception error) when (deadline.IsCancellationRequested && (error is HttpRequestException or IOException))
        { throw new LlmProviderException(LlmProviderFailureKind.Timeout); }
        catch (OperationCanceledException) { throw new LlmProviderException(LlmProviderFailureKind.Transport); }
        catch (HttpRequestException error) { throw ProviderFailures.Transport(error); }
        catch (IOException error) { throw ProviderFailures.Transport(error); }
        catch (JsonException) { throw OpenAiProtocol.Invalid(); }
        catch (DecoderFallbackException) { throw OpenAiProtocol.Invalid(); }
    }

    private static T Parse<T>(Func<T> parse)
    {
        try { return parse(); }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { throw OpenAiProtocol.Invalid(); }
    }

    public void Dispose() => client.Dispose();
}
