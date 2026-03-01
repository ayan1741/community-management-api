namespace CommunityManagement.Core.Entities;

public class DuesPeriod
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public DateOnly StartDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Status { get; set; } = default!;  // draft|processing|active|failed|closed
    public Guid CreatedBy { get; set; }
    public DateTimeOffset? ReminderSentAt { get; set; }
    public DateTimeOffset? ClosedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
