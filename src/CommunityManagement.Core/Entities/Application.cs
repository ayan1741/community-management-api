using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Entities;

public class Application
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UnitId { get; set; }
    public Guid? InvitationId { get; set; }
    public Guid ApplicantUserId { get; set; }
    public ResidentType ApplicantResidentType { get; set; }
    public ApplicationStatus ApplicationStatus { get; set; }
    public string? RejectionReason { get; set; }
    public Guid? ReviewedBy { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
