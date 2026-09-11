using IndustrialCopilot.Domain.Diagnostics;
using IndustrialCopilot.Domain.Evidence;
using IndustrialCopilot.Domain.Manuals;

namespace IndustrialCopilot.Domain.Tests.Diagnostics;

public class DiagnosticStepTests
{
    private static EvidenceReference Evidence() => new(
        new Manual(Guid.NewGuid(), Guid.NewGuid(), "Manual").PublishRevision(Guid.NewGuid(), 1), "Page 12");

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonPositiveOrder(int order) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiagnosticStep(order, "Inspect coupling", [Evidence()]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingInstruction(string? instruction) =>
        Assert.ThrowsAny<ArgumentException>(() => new DiagnosticStep(1, instruction!, [Evidence()]));

    [Fact]
    public void RejectsMissingOrNullEvidence()
    {
        Assert.Throws<ArgumentNullException>(() => new DiagnosticStep(1, "Inspect coupling", null!));
        Assert.Throws<ArgumentException>(() => new DiagnosticStep(1, "Inspect coupling", []));
        Assert.Throws<ArgumentException>(() => new DiagnosticStep(1, "Inspect coupling", [Evidence(), null!]));
    }

    [Fact]
    public void PreservesInstructionOrderAndOwnsEvidenceCollection()
    {
        var first = Evidence();
        var second = Evidence();
        var input = new List<EvidenceReference> { first, second };
        var step = new DiagnosticStep(2, "Inspect coupling", input);
        input[0] = Evidence();
        input.Clear();
        Assert.Equal(2, step.Order);
        Assert.Equal("Inspect coupling", step.Instruction);
        Assert.Equal(new[] { first, second }, step.Evidence);
        Assert.Throws<NotSupportedException>(() => ((IList<EvidenceReference>)step.Evidence).Clear());
        Assert.Equal(first.ManualRevisionId, step.Evidence[0].ManualRevisionId);
    }

    [Fact]
    public void EqualityComparesEvidenceValuesInsteadOfCollectionIdentity()
    {
        var first = Evidence();
        var second = Evidence();
        var step = new DiagnosticStep(1, "Inspect coupling", [first, second]);
        var same = new DiagnosticStep(1, "Inspect coupling", [first with { }, second with { }]);
        Assert.Equal(step, same);
        Assert.Equal(step.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(step, new DiagnosticStep(2, "Inspect coupling", [first, second]));
        Assert.NotEqual(step, new DiagnosticStep(1, "Inspect seal", [first, second]));
        Assert.NotEqual(step, new DiagnosticStep(1, "Inspect coupling", [second, first]));
        Assert.NotEqual(step, new DiagnosticStep(1, "Inspect coupling", [first]));
    }
}
