namespace CommunityManagement.Core.Entities;

public class Unit
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid BlockId { get; set; }
    public string UnitNumber { get; set; } = default!;
    public string UnitType { get; set; } = default!;  // 'residential' | 'shop' | 'storage' | 'parking' | 'other'
    public int? Floor { get; set; }
    public decimal? AreaSqm { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
