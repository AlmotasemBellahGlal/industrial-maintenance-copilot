using IndustrialCopilot.Domain.Manuals;

namespace IndustrialCopilot.Domain.Evidence;

public sealed record EvidenceReference
{
    public Guid ManualId { get; }
    public Guid ManualRevisionId { get; }
    public string SourceLocator { get; }

    public EvidenceReference(ManualRevision revision, string sourceLocator)
    {
        ArgumentNullException.ThrowIfNull(revision);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocator);
        ManualId = revision.ManualId;
        ManualRevisionId = revision.Id;
        SourceLocator = sourceLocator;
    }
}
