namespace CommunityManagement.Core.Entities;

public class FinanceCategory
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!; // "income" | "expense"
    public Guid? ParentId { get; set; }
    public string? Icon { get; set; }
    public bool IsSystem { get; set; }
    public bool IsActive { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
