namespace IndustrialCopilot.Domain.WorkOrders;

public sealed record WorkOrderAction
{
    public int Order { get; }
    public string Instruction { get; }

    public WorkOrderAction(int order, string instruction)
    {
        if (order <= 0) throw new ArgumentOutOfRangeException(nameof(order));
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        Order = order;
        Instruction = instruction;
    }
}
