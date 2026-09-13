namespace IndustrialCopilot.Infrastructure.Knowledge;

public enum KnowledgeFailure { StorageUnavailable, IncompatibleEmbeddingSpace }
public sealed class KnowledgeStoreException : Exception
{
    public KnowledgeFailure Kind { get; }
    internal KnowledgeStoreException(KnowledgeFailure kind) : base($"Knowledge operation failed ({kind}).") => Kind = kind;
}
