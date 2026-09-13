using System.Text.Json;
using IndustrialCopilot.Application.Abstractions.Agents;
using IndustrialCopilot.Application.Abstractions.Agents.Evidence;

namespace IndustrialCopilot.Application.Reasoning;

internal static class AgentJson
{
    internal static JsonDocument Parse(string text)
    {
        if (text.Length > 65536) throw new InvalidAgentOutputException();
        try { return JsonDocument.Parse(text,new JsonDocumentOptions { MaxDepth=16 }); }
        catch (JsonException) { throw new InvalidAgentOutputException(); }
    }
    internal static void Shape(JsonElement value, params string[] keys)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidAgentOutputException();
        var names = value.EnumerateObject().Select(p => p.Name).ToArray();
        if (names.Length != keys.Length || names.Distinct().Count() != names.Length || names.Except(keys).Any()) throw new InvalidAgentOutputException();
    }
    internal static string Text(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 4000) throw new InvalidAgentOutputException();
        return value.GetString()!;
    }
    internal static int Number(JsonElement value)
    {
        if (!value.TryGetInt32(out var number)) throw new InvalidAgentOutputException();
        return number;
    }
    internal static JsonElement[] Array(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > 32) throw new InvalidAgentOutputException();
        return value.EnumerateArray().ToArray();
    }
    internal static AgentOutcome Outcome(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("outcome",out var outcome)) throw new InvalidAgentOutputException();
        return Text(outcome) switch {
            "Success" => AgentOutcome.Success,
            "InsufficientEvidence" => AgentOutcome.InsufficientEvidence,
            "CannotProceed" => AgentOutcome.CannotProceed,
            _ => throw new InvalidAgentOutputException()
        };
    }
    internal static bool IsInvalid(Exception e) => e is InvalidAgentOutputException or JsonException or ArgumentException or InvalidOperationException or FormatException or KeyNotFoundException;
}

/// <summary>Model outputs carry opaque references only; all provenance bytes come from the trusted catalog.</summary>
internal sealed class EvidenceCatalog
{
    internal sealed record Entry(string Reference, Guid DocumentId, Guid ManualRevisionId, Guid ChunkId, string Locator, string Snippet);
    private readonly List<GroundedEvidence> items = [];
    internal void Add(GroundedEvidence item)
    {
        if(items.Count>=256 || item.Snippet.Length>16000 || item.Locator.Length>4000) throw new InvalidAgentOutputException();
        var old = items.SingleOrDefault(e => e.DocumentId==item.DocumentId && e.ManualRevisionId==item.ManualRevisionId && e.ChunkId==item.ChunkId);
        if (old is not null && old != item) throw new InvalidAgentOutputException();
        if (old is null) items.Add(item);
    }
    internal Entry[] Describe() => items.Select((e,i) => new Entry("e"+i,e.DocumentId,e.ManualRevisionId,e.ChunkId,e.Locator,e.Snippet)).ToArray();
    internal IReadOnlyList<GroundedEvidence> Resolve(JsonElement references)
    {
        var keys = AgentJson.Array(references).Select(AgentJson.Text).ToArray();
        if (keys.Length == 0 || keys.Distinct().Count() != keys.Length) throw new InvalidAgentOutputException();
        return keys.Select(key => {
            if (key.Length < 2 || key[0]!='e' || !int.TryParse(key.AsSpan(1),out var i) || i<0 || i>=items.Count || key!="e"+i) throw new InvalidAgentOutputException();
            return items[i];
        }).ToArray();
    }
}
