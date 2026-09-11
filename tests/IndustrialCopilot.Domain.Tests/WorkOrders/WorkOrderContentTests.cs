using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Domain.Tests.WorkOrders;

public class WorkOrderContentTests
{
    private static readonly Guid Equipment = Guid.NewGuid();
    private static readonly Guid Manual = Guid.NewGuid();
    private static readonly Guid Revision = Guid.NewGuid();
    private static WorkOrderContent Create(IEnumerable<WorkOrderAction> actions) =>
        new(Equipment, Manual, Revision, "Vibration", "Repair coupling", actions);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsEmptyIdentities(int field) =>
        Assert.Throws<ArgumentException>(() => new WorkOrderContent(
            field == 0 ? Guid.Empty : Equipment, field == 1 ? Guid.Empty : Manual,
            field == 2 ? Guid.Empty : Revision, "Vibration", "Repair", [new(1, "Replace")]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingText(string? text)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderContent(Equipment, Manual, Revision, text!, "Repair", [new(1, "Replace")]));
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderContent(Equipment, Manual, Revision, "Vibration", text!, [new(1, "Replace")]));
    }

    [Fact]
    public void RejectsMissingOrUnorderedActions()
    {
        Assert.Throws<ArgumentNullException>(() => Create(null!));
        Assert.Throws<ArgumentException>(() => Create([]));
        Assert.Throws<ArgumentException>(() => Create([null!]));
        Assert.Throws<ArgumentException>(() => Create([new(1, "A"), new(1, "B")]));
        Assert.Throws<ArgumentException>(() => Create([new(2, "A"), new(1, "B")]));
    }

    [Fact]
    public void OwnsMultiActionSequenceAndUsesValueEquality()
    {
        var actions = new List<WorkOrderAction> { new(1, "Remove seal"), new(3, "Install seal") };
        var content = Create(actions);
        var equal = Create([new(1, "Remove seal"), new(3, "Install seal")]);
        actions.Clear();
        Assert.Equal(equal, content);
        Assert.Equal(equal.GetHashCode(), content.GetHashCode());
        Assert.Equal(2, content.Actions.Count);
        Assert.Throws<NotSupportedException>(() => ((IList<WorkOrderAction>)content.Actions).Clear());
        Assert.NotEqual(content, Create([new(1, "Other work")]));
        Assert.NotEqual(content, new WorkOrderContent(Guid.NewGuid(), Manual, Revision, content.ReportedSymptom, content.Description, content.Actions));
        Assert.NotEqual(content, new WorkOrderContent(Equipment, Guid.NewGuid(), Revision, content.ReportedSymptom, content.Description, content.Actions));
        Assert.NotEqual(content, new WorkOrderContent(Equipment, Manual, Guid.NewGuid(), content.ReportedSymptom, content.Description, content.Actions));
        Assert.NotEqual(content, new WorkOrderContent(Equipment, Manual, Revision, "Noise", content.Description, content.Actions));
        Assert.NotEqual(content, new WorkOrderContent(Equipment, Manual, Revision, content.ReportedSymptom, "Other scope", content.Actions));
    }
}
