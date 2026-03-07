namespace CommunityManagement.Core.Entities;

public class AgendaItem
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? MeetingId { get; set; }
    public Guid CreatedBy { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public string Category { get; set; } = default!;     // genel|bakim_onarim|guvenlik|sosyal|finansal|yonetim
    public string Status { get; set; } = default!;       // acik|degerlendiriliyor|oylamada|kararlasti|kapali
    public bool IsPinned { get; set; }
    public string? CloseReason { get; set; }
    public int SupportCount { get; set; }
    public int CommentCount { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
