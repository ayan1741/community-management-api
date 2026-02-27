using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Entities;

public class Invitation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UnitId { get; set; }
    public string InvitationCode { get; set; } = default!;
    public CodeStatus CodeStatus { get; set; }
    public Guid CreatedBy { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
