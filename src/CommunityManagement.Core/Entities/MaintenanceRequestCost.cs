namespace CommunityManagement.Core.Entities;

public class MaintenanceRequestCost
{
    public Guid Id { get; set; }
    public Guid MaintenanceRequestId { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public Guid? FinanceRecordId { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
