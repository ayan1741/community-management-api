namespace CommunityManagement.Core.Entities;

public class UnitDue
{
    public Guid Id { get; set; }
    public Guid PeriodId { get; set; }
    public Guid UnitId { get; set; }
    public Guid DueTypeId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = default!;  // pending|partial|paid|cancelled
    public Guid CreatedBy { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
