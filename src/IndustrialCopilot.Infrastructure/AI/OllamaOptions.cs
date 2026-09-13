namespace IndustrialCopilot.Infrastructure.AI;

public sealed class OllamaOptions
{
    public Uri Endpoint { get; }
    public string ChatModel { get; }
    public string EmbeddingModel { get; }
    public TimeSpan RequestTimeout { get; }
    public TimeSpan StreamingTimeout { get; }

    public OllamaOptions(Uri endpoint, string chatModel, string embeddingModel,
        TimeSpan requestTimeout, TimeSpan streamingTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!endpoint.IsAbsoluteUri ||
            !(endpoint.Scheme == Uri.UriSchemeHttps || (endpoint.Scheme == Uri.UriSchemeHttp && endpoint.IsLoopback)) ||
            endpoint.AbsolutePath != "/" || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new ArgumentException("Ollama requires a root HTTPS endpoint, or loopback HTTP endpoint.", nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(chatModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        if (requestTimeout <= TimeSpan.Zero || requestTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(requestTimeout));
        if (streamingTimeout <= TimeSpan.Zero || streamingTimeout.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(streamingTimeout));
        Endpoint = endpoint;
        ChatModel = chatModel;
        EmbeddingModel = embeddingModel;
        RequestTimeout = requestTimeout;
        StreamingTimeout = streamingTimeout;
    }
}
