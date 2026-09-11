using IndustrialCopilot.Application.Abstractions.Tracing.Models;

namespace IndustrialCopilot.Application.Abstractions.Tracing;

/// <summary>Stores complete versioned execution snapshots without granting workflow authority.</summary>
/// <remarks>
/// The trusted writer merges child observations into a complete snapshot. Active steps need no end,
/// so running/waiting work can be recorded incrementally. No generic event sourcing is implied.
/// Producers must sanitize names and errors; never require prompts, secrets or raw provider payloads.
/// Implementations enforce access policy and return immutable snapshots. No IQueryable or SDK types.
/// CancellationToken cancels this storage operation, not the traced run. Persisted Cancelled status
/// is an observation. Cancellation after an uncertain write does not guarantee rollback.
/// </remarks>
public interface IRunTraceStore
{
    /// <summary>Atomically writes the complete next snapshot, or returns false on version conflict.</summary>
    /// <remarks>
    /// Snapshot is non-null. ExpectedVersion is nonnegative; zero means no existing execution.
    /// Snapshot.Version must equal checked(expectedVersion + 1). Compare and replace atomically:
    /// never merge concurrent snapshots into a union or silently overwrite a newer version.
    /// A retry of an already stored structurally equivalent version returns true without duplication;
    /// do not use collection reference equality to establish equivalence. Otherwise stale writes return false.
    /// Preserve execution/correlation IDs, existing steps and their identity/parent/kind/start time.
    /// A Domain run association may be established from null but cannot be changed once recorded.
    /// Terminal step status/end cannot be reopened or rewritten; late usage/cost may fill missing data.
    /// No existing observations may silently disappear. Enforce these consistency rules before mutation.
    /// Technical failures throw; cancellation uses OperationCanceledException. Retrying never adds token/cost totals:
    /// totals are derived from distinct Llm steps in the stored snapshot. This is not an approval audit authority.
    /// </remarks>
    Task<bool> TrySaveAsync(RunTraceSnapshot snapshot, long expectedVersion, CancellationToken cancellationToken);

    /// <summary>Reads the latest complete snapshot, or null if absent. ExecutionId must be nonempty.</summary>
    Task<RunTraceSnapshot?> GetAsync(Guid executionId, CancellationToken cancellationToken);
}
