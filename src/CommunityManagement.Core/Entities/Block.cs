namespace CommunityManagement.Core.Entities;

public class Block
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public string BlockType { get; set; } = default!;  // 'residential' | 'commercial' | 'mixed'
    public bool IsDefault { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
