using System.Text.Json;

namespace IndustrialCopilot.Application.Abstractions.AI.Models;

public sealed record ToolCall
{
    public string Id { get; }
    public string Name { get; }
    public JsonElement Arguments { get; }

    public ToolCall(string id, string name, JsonElement arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (arguments.ValueKind != JsonValueKind.Object) throw new ArgumentException("Arguments must be a JSON object.", nameof(arguments));
        Id = id;
        Name = name;
        Arguments = arguments.Clone();
    }
}
