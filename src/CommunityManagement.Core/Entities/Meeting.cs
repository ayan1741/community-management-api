namespace CommunityManagement.Core.Entities;

public class Meeting
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public DateTimeOffset MeetingDate { get; set; }
    public string Status { get; set; } = default!;  // planlanmis|tamamlandi|iptal
    public Guid CreatedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
