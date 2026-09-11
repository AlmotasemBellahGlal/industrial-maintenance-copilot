using IndustrialCopilot.Domain.Evidence;
using IndustrialCopilot.Domain.Manuals;

namespace IndustrialCopilot.Domain.Tests.Evidence;

public class EvidenceReferenceTests
{
    private static ManualRevision Revision() =>
        new Manual(Guid.NewGuid(), Guid.NewGuid(), "Manual").PublishRevision(Guid.NewGuid(), 1);

    [Fact]
    public void RequiresPublishedRevision() =>
        Assert.Throws<ArgumentNullException>(() => new EvidenceReference(null!, "Page 12"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingLocator(string? locator) =>
        Assert.ThrowsAny<ArgumentException>(() => new EvidenceReference(Revision(), locator!));

    [Fact]
    public void PreservesExactSourceAndUsesValueEquality()
    {
        var revision = Revision();
        var evidence = new EvidenceReference(revision, "Page 12, section 3");
        var same = new EvidenceReference(revision, "Page 12, section 3");
        Assert.Equal(revision.ManualId, evidence.ManualId);
        Assert.Equal(revision.Id, evidence.ManualRevisionId);
        Assert.Equal("Page 12, section 3", evidence.SourceLocator);
        Assert.Equal(evidence, same);
        Assert.Equal(evidence.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(evidence, new EvidenceReference(revision, "Page 13"));
        Assert.NotEqual(evidence, new EvidenceReference(Revision(), "Page 12, section 3"));
    }
}
