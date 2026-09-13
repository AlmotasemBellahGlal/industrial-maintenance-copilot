namespace IndustrialCopilot.Infrastructure.AI;

public enum LlmProviderFailureKind
{
    Transport,
    Timeout,
    Authentication,
    RateLimited,
    InvalidRequest,
    Unavailable,
    InvalidResponse,
    UnsupportedResponse,
    Refused,
    HttpFailure,
    TransientTransport,
    TemporaryRateLimit,
    QuotaExceeded
}

/// <summary>A sanitized Infrastructure boundary. Contains no response body or inner exception.
/// Failure kinds are not a fallback policy.</summary>
public sealed class LlmProviderException : Exception
{
    public LlmProviderFailureKind Kind { get; }

    internal LlmProviderException(LlmProviderFailureKind kind)
        : base($"LLM provider operation failed ({kind}).") => Kind = kind;
}
