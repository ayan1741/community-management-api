namespace CommunityManagement.Core.Entities;

public class Notification
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Type { get; set; } = default!;          // announcement|due_reminder|payment|application
    public string Title { get; set; } = default!;
    public string? Body { get; set; }
    public string? ReferenceType { get; set; }            // announcement|unit_due|payment
    public Guid? ReferenceId { get; set; }
    public bool IsRead { get; set; }
    public DateTimeOffset? ReadAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
