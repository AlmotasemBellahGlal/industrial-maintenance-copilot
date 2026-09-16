using IndustrialCopilot.Application.Abstractions.Retrieval.Models;
namespace IndustrialCopilot.Application.Ask;

public enum AskState { Streaming, Completed, InsufficientEvidence, Cancelled, Failed }
public sealed record Conversation(Guid Id, Guid EquipmentId, Guid DocumentId, Guid ManualRevisionId,
    string Culture, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record AskTurn(Guid Id, long Sequence, string Question, string Answer, AskState State,
    IReadOnlyList<AskCitation> Citations, Guid CorrelationId, DateTimeOffset CreatedAt);
/// <summary>Trusted retrieval provenance, never parsed from model-generated identifiers.</summary>
public sealed record AskCitation(Guid DocumentId, Guid ManualRevisionId, Guid ChunkId, string Locator, string Snippet)
{
    public static AskCitation From(RetrievalResult r) => new(r.DocumentId,r.ManualRevisionId,r.ChunkId,r.Locator,r.Snippet);
}
public sealed record AskEvent(string Type, string? Delta = null, IReadOnlyList<AskCitation>? Citations = null, AskState? State = null);
public sealed record AskQuestion
{
    public string Text { get; }
    public int TopK { get; }
    public AskQuestion(string text, int topK = 5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        if(text.Length > 2000) throw new ArgumentException("Question too long.");
        if(topK is < 1 or > 10) throw new ArgumentOutOfRangeException(nameof(topK));
        Text=text.Trim(); TopK=topK;
    }
}
/// <summary>Trusted host supplies actor. Implementations enforce owner matching on every access.
/// One active turn per conversation; expired turns become Failed, never silently replayed.</summary>
public interface IConversationStore
{
    Task<Conversation> CreateAsync(string actor, Guid equipment, Guid document, Guid revision, string culture, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(string actor, IReadOnlyCollection<Guid> permittedEquipment, int offset, int limit, CancellationToken ct);
    Task<Conversation?> GetAsync(string actor, Guid id, CancellationToken ct);
    Task<IReadOnlyList<AskTurn>> TurnsAsync(string actor, Guid id, long after, int limit, CancellationToken ct);
    Task<Guid?> BeginAsync(string actor, Guid id, string safeQuestion, Guid correlation, CancellationToken ct);
    Task<bool> FinishAsync(string actor, Guid id, Guid turn, AskState state, string safeAnswer, IReadOnlyList<AskCitation> citations, CancellationToken ct);
}
/// <summary>Sanitizes human text for history, never changes citation identities or source provenance.</summary>
public interface IHistoryText { string Sanitize(string text); }
