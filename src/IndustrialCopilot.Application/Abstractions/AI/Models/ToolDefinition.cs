using System.Text.Json;

namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record ToolDefinition
{
    public string Name { get; }
    public string Description { get; }
    public JsonElement ArgumentsSchema { get; }

    public ToolDefinition(string name, string description, JsonElement argumentsSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        if (argumentsSchema.ValueKind != JsonValueKind.Object) throw new ArgumentException("Argument schema must be a JSON object.", nameof(argumentsSchema));
        Name = name;
        Description = description;
        // Own the JSON independently of the caller's JsonDocument lifetime.
        ArgumentsSchema = argumentsSchema.Clone();
    }
}
