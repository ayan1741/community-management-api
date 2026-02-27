using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Entities;

public class OrganizationMember
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public MemberRole Role { get; set; }
    public MemberStatus Status { get; set; }
    public Guid? InvitedBy { get; set; }
    public DateTimeOffset? SuspendedAt { get; set; }
    public Guid? SuspendedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
