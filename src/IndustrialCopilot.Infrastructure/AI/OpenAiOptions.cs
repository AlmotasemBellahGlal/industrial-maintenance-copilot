namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>Supply the API key from a trusted host's environment/configuration secret provider.
/// This type deliberately has no record-generated representation containing the key.</summary>
public sealed class OpenAiOptions
{
    public Uri Endpoint { get; }
    public string ChatModel { get; }
    public string EmbeddingModel { get; }
    internal string ApiKey { get; }
    public TimeSpan RequestTimeout { get; }
    public TimeSpan StreamingTimeout { get; }

    public OpenAiOptions(Uri endpoint, string chatModel, string embeddingModel, string apiKey,
        TimeSpan requestTimeout, TimeSpan streamingTimeout)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        // This is the hosted OpenAI adapter, not an arbitrary compatible proxy.
        if (!endpoint.IsAbsoluteUri || endpoint.Scheme != Uri.UriSchemeHttps ||
            endpoint.Host != "api.openai.com" || !endpoint.IsDefaultPort ||
            endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 ||
            endpoint.Fragment.Length != 0 || endpoint.UserInfo.Length != 0)
            throw new ArgumentException("The hosted OpenAI HTTPS root endpoint is required.", nameof(endpoint));
        ArgumentException.ThrowIfNullOrWhiteSpace(chatModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(embeddingModel);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        if (apiKey.Any(c => !char.IsAsciiLetterOrDigit(c) && !"-._~+/=".Contains(c)))
            throw new ArgumentException("The API key contains invalid characters.", nameof(apiKey));
        ValidateTimeout(requestTimeout, nameof(requestTimeout));
        ValidateTimeout(streamingTimeout, nameof(streamingTimeout));
        Endpoint = endpoint;
        ChatModel = chatModel;
        EmbeddingModel = embeddingModel;
        ApiKey = apiKey;
        RequestTimeout = requestTimeout;
        StreamingTimeout = streamingTimeout;
    }

    private static void ValidateTimeout(TimeSpan value, string parameter)
    {
        if (value <= TimeSpan.Zero || value.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(parameter);
    }
}
