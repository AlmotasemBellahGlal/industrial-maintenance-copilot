namespace IndustrialCopilot.Application.Abstractions.Tracing.Models;

/// <summary>Sanitized diagnostic information. The caller must redact secrets and personal data.</summary>
/// <remarks>No raw exceptions, stack traces, prompts or provider payloads are required or accepted as structured fields.</remarks>
public sealed record TraceError
{
    public string Code { get; }
    public string SafeMessage { get; }

    public TraceError(string code, string safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        Code = code;
        SafeMessage = safeMessage;
    }
}
