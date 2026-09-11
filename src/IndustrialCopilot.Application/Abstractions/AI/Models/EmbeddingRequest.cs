namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record EmbeddingRequest
{
    public IReadOnlyList<string> Inputs { get; }

    public EmbeddingRequest(IReadOnlyList<string> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        var snapshot = inputs.ToArray();
        if (snapshot.Any(input => input is null)) throw new ArgumentException("Inputs cannot contain null.", nameof(inputs));
        Inputs = Array.AsReadOnly(snapshot);
    }
}
