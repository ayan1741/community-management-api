namespace CommunityManagement.Core.Entities;

public class DueType
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public decimal DefaultAmount { get; set; }
    public string? CategoryAmounts { get; set; }  // JSON string — handler parse eder
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
