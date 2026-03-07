namespace CommunityManagement.Core.Entities;

public class MaintenanceRequestLog
{
    public Guid Id { get; set; }
    public Guid MaintenanceRequestId { get; set; }
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = default!;
    public string? Note { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
