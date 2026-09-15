using IndustrialCopilot.Application.Abstractions.Documents.Models;

namespace IndustrialCopilot.Application.Abstractions.Documents;

public enum IngestionStage { Extracting = 1, Cleaning, Chunking, Embedding, Indexing }
public enum IngestionState { Processing = 1, Completed, Failed, Interrupted }
public enum IngestionFailure { UnsupportedFormat = 1, InvalidDocument, InputTooLarge, EmptyDocument, Cancelled, StageFailed }
public sealed record IngestionReport(Guid AttemptId, Guid DocumentId, Guid ManualRevisionId,
    DocumentMetadata? Metadata, IngestionState State, IngestionStage Stage, IngestionFailure? Failure,
    DateTimeOffset StartedAt, DateTimeOffset UpdatedAt, int? Pages, int? Chunks);

/// <summary>One independent durable attempt. Completion must be committed by the index with its revision replacement.</summary>
public interface IIngestionAttempt : IAsyncDisposable
{
    Guid Id { get; }
    Task AdvanceAsync(IngestionStage stage, int? pages, int? chunks, CancellationToken token);
    Task FailAsync(IngestionFailure failure, CancellationToken token);
}
public interface IIngestionReports
{
    Task<IIngestionAttempt> BeginAsync(DocumentProcessingRequest request, CancellationToken token);
    Task<IReadOnlyList<IngestionReport>> ReadAsync(Guid documentId, Guid revisionId, CancellationToken token);
}
/// <summary>Safe extraction category; never includes parser internals.</summary>
public sealed class DocumentInputException(IngestionFailure failure) : InvalidOperationException("Document input was rejected.")
{
    public IngestionFailure Failure { get; } = failure;
}
