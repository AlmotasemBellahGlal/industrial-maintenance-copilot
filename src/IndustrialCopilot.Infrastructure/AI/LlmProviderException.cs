using IndustrialCopilot.Application.Abstractions.AI;
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
public sealed class LlmProviderException : DependencyFailureException
{
    public LlmProviderFailureKind Kind { get; }

    internal LlmProviderException(LlmProviderFailureKind kind)
        : base(kind is LlmProviderFailureKind.TransientTransport or LlmProviderFailureKind.TemporaryRateLimit or LlmProviderFailureKind.Unavailable ? DependencyFailureKind.Transient : kind==LlmProviderFailureKind.Timeout ? DependencyFailureKind.Timeout : DependencyFailureKind.Terminal) => Kind = kind;
}
