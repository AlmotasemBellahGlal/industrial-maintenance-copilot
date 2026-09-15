using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace IndustrialCopilot.Infrastructure.AI;

/// <summary>Outbound copy only. Local evidence, identifiers and executable definitions are never rewritten.</summary>
internal static class HostedDataBoundary
{
    private static readonly Regex Sensitive = new(
        @"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}|\bBearer\s+[A-Z0-9._~+/=-]{8,}|\b(?:api[_ -]?key|password|secret|national[_ -]?id|ssn)\s*[:=]\s*[""']?[A-Z0-9._~+/@=-]{4,}|\b\d{3}-\d{2}-\d{4}\b|(?<![\w-])(?:\+\d{1,3}[ -]?)?\(?\d{3}\)?[ -]\d{3}[ -]\d{4}(?![\w-])|(?<![\w-])\+\d{10,15}(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string Redact(string text, string? key = null)
    {
        if (!string.IsNullOrEmpty(key)) text = text.Replace(key, "[REDACTED]", StringComparison.Ordinal);
        return Sensitive.Replace(text, "[REDACTED]");
    }

    private static string Structured(string text, string? key)
    {
        try
        {
            var node = JsonNode.Parse(text);
            if (node is JsonObject or JsonArray) { Walk(node, key); return node.ToJsonString(); }
        }
        catch (JsonException) { }
        return Redact(text, key);
    }
    private static void Walk(JsonNode node, string? key)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    obj[property.Key] = property.Key.ToLowerInvariant() is "password" or "secret" or "api_key" or "apikey" or "national_id" or "ssn"
                        ? "[REDACTED]" : Redact(text, key);
                else if (property.Value is {} child) Walk(child, key);
            }
        else if (node is JsonArray array)
            for (var i = 0; i < array.Count; i++)
                if (array[i] is JsonValue value && value.TryGetValue<string>(out var text)) array[i] = Redact(text, key);
                else if (array[i] is {} child) Walk(child, key);
    }

    internal static void Apply(JsonObject payload, string? key)
    {
        if (payload["messages"] is JsonArray messages)
            foreach (var item in messages.OfType<JsonObject>())
            {
                if (item["content"] is JsonValue content) item["content"] = Structured(content.GetValue<string>(), key);
                if (item["tool_calls"] is JsonArray calls)
                    foreach (var call in calls.OfType<JsonObject>())
                        if (call["function"] is JsonObject function && function["arguments"] is JsonValue arguments)
                            function["arguments"] = Structured(arguments.GetValue<string>(), key);
            }
        if (payload["input"] is JsonArray inputs)
            for (var i = 0; i < inputs.Count; i++) inputs[i] = Redact(inputs[i]!.GetValue<string>(), key);
    }
}
