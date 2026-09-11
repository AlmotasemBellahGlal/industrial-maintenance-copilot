using IndustrialCopilot.Domain.WorkOrders;

namespace IndustrialCopilot.Domain.Tests.WorkOrders;

public class WorkOrderActionTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsNonpositiveOrder(int order) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkOrderAction(order, "Replace seal"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingInstruction(string? instruction) =>
        Assert.ThrowsAny<ArgumentException>(() => new WorkOrderAction(1, instruction!));

    [Fact]
    public void EqualityIncludesOrderAndInstruction()
    {
        var action = new WorkOrderAction(1, "Replace seal");
        var equal = new WorkOrderAction(1, "Replace seal");
        Assert.Equal(action, equal);
        Assert.Equal(action.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(action, new WorkOrderAction(2, "Replace seal"));
        Assert.NotEqual(action, new WorkOrderAction(1, "Replace bearing"));
    }
}
