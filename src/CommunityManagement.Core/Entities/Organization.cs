namespace CommunityManagement.Core.Entities;

public class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string OrgType { get; set; } = default!;
    public string Status { get; set; } = default!;
    public Guid CreatedBy { get; set; }
    public string? AddressDistrict { get; set; }
    public string? AddressCity { get; set; }
    public string? ContactPhone { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
