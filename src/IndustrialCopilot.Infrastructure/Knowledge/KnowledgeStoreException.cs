using IndustrialCopilot.Application.Abstractions.AI;
namespace IndustrialCopilot.Infrastructure.Knowledge;

public enum KnowledgeFailure { StorageUnavailable, IncompatibleEmbeddingSpace }
public sealed class KnowledgeStoreException : DependencyFailureException
{
    public KnowledgeFailure Kind { get; }
    internal KnowledgeStoreException(KnowledgeFailure kind, DependencyFailureKind failure=DependencyFailureKind.Terminal) : base(failure) => Kind = kind;
}
