namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record CompletionRequest
{
    public IReadOnlyList<LlmMessage> Messages { get; }
    public double? Temperature { get; }
    public int? MaxTokens { get; }

    public CompletionRequest(IReadOnlyList<LlmMessage> messages, double? temperature = null, int? maxTokens = null)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var snapshot = messages.ToArray();
        if (snapshot.Any(message => message is null)) throw new ArgumentException("Messages cannot contain null.", nameof(messages));
        if (temperature is { } value && !double.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(temperature));
        if (maxTokens is <= 0) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        Messages = Array.AsReadOnly(snapshot);
        Temperature = temperature;
        MaxTokens = maxTokens;
    }
}
