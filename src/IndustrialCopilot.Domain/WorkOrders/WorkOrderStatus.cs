namespace IndustrialCopilot.Domain.WorkOrders;

public enum WorkOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    Dispatched = 5
}