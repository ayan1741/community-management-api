namespace CommunityManagement.Core.Entities;

public class Announcement
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Title { get; set; } = default!;
    public string Body { get; set; } = default!;
    public string Category { get; set; } = default!;     // general|urgent|maintenance|meeting|financial|other
    public string Priority { get; set; } = default!;     // normal|important|urgent
    public string TargetType { get; set; } = default!;   // all|block|role
    public string? TargetIds { get; set; }                // JSON string — ["uuid1","uuid2"] veya ["admin","board_member"]
    public string Status { get; set; } = default!;       // draft|published|expired
    public bool IsPinned { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string? AttachmentUrls { get; set; }           // JSON string — [{url,name,size}]
    public int? TargetMemberCount { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
