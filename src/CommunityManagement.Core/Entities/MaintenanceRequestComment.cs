namespace CommunityManagement.Core.Entities;

public class MaintenanceRequestComment
{
    public Guid Id { get; set; }
    public Guid MaintenanceRequestId { get; set; }
    public string Content { get; set; } = default!;
    public string? PhotoUrl { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
