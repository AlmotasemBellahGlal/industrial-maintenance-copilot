using IndustrialCopilot.Domain.Manuals;
using IndustrialCopilot.Domain.Evidence;

namespace IndustrialCopilot.Domain.Tests.Manuals;

public class ManualTests
{
    private static Manual Create() => new(Guid.NewGuid(), Guid.NewGuid(), "Pump service manual");

    [Fact]
    public void PreservesManualAndPublishedRevisionIdentities()
    {
        var id = Guid.NewGuid();
        var equipmentId = Guid.NewGuid();
        var manual = new Manual(id, equipmentId, "Pump service manual");
        var revisionId = Guid.NewGuid();
        var revision = manual.PublishRevision(revisionId, 1);
        var evidence = new EvidenceReference(revision, "Page 12, section 3");
        manual.PublishRevision(Guid.NewGuid(), 2);
        Assert.Equal(id, manual.Id);
        Assert.Equal(equipmentId, manual.EquipmentId);
        Assert.Equal("Pump service manual", manual.Title);
        Assert.Same(revision, manual.Revisions[0]);
        Assert.Equal(revisionId, revision.Id);
        Assert.Equal(id, revision.ManualId);
        Assert.Equal(1, revision.Number);
        Assert.Equal(id, evidence.ManualId);
        Assert.Equal(revisionId, evidence.ManualRevisionId);
        Assert.Equal(2, manual.Revisions.Count);
    }

    [Fact]
    public void RejectsEmptyIdentities()
    {
        Assert.Throws<ArgumentException>(() => new Manual(Guid.Empty, Guid.NewGuid(), "Manual"));
        Assert.Throws<ArgumentException>(() => new Manual(Guid.NewGuid(), Guid.Empty, "Manual"));
        var manual = Create();
        Assert.Throws<ArgumentException>(() => manual.PublishRevision(Guid.Empty, 1));
        Assert.Empty(manual.Revisions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingTitle(string? title) =>
        Assert.ThrowsAny<ArgumentException>(() => new Manual(Guid.NewGuid(), Guid.NewGuid(), title!));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidRevisionNumberWithoutAddingRevision(int number)
    {
        var manual = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => manual.PublishRevision(Guid.NewGuid(), number));
        Assert.Empty(manual.Revisions);
    }

    [Fact]
    public void RejectsDuplicateRevisionIdentityOrNumberWithoutMutation()
    {
        var manual = Create();
        var original = manual.PublishRevision(Guid.NewGuid(), 1);
        Assert.Throws<InvalidOperationException>(() => manual.PublishRevision(original.Id, 2));
        Assert.Throws<InvalidOperationException>(() => manual.PublishRevision(Guid.NewGuid(), 1));
        Assert.Same(original, Assert.Single(manual.Revisions));
    }

    [Fact]
    public void RevisionNumbersAreScopedToTheirManual()
    {
        Assert.Equal(1, Create().PublishRevision(Guid.NewGuid(), 1).Number);
        Assert.Equal(1, Create().PublishRevision(Guid.NewGuid(), 1).Number);
    }

    [Fact]
    public void PublishedRevisionsCannotBeRemovedThroughExposedCollection()
    {
        var manual = Create();
        var revision = manual.PublishRevision(Guid.NewGuid(), 1);
        Assert.Throws<NotSupportedException>(() => ((IList<ManualRevision>)manual.Revisions).Clear());
        Assert.Same(revision, Assert.Single(manual.Revisions));
    }
}
