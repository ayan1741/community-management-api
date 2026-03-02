using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Entities;

public class UnitResident
{
    public Guid Id { get; set; }
    public Guid UnitId { get; set; }
    public Guid UserId { get; set; }
    public Guid OrganizationId { get; set; }
    public ResidentType ResidentType { get; set; }
    public bool IsPrimary { get; set; }
    public UnitResidentStatus Status { get; set; } = UnitResidentStatus.Active;
    public DateTimeOffset? RemovedAt { get; set; }
    public Guid? RemovedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
