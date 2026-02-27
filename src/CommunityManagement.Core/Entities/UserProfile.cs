namespace CommunityManagement.Core.Entities;

public class UserProfile
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = default!;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
    public DateTimeOffset? KvkkConsentAt { get; set; }
    public DateTimeOffset? DeletionRequestedAt { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
