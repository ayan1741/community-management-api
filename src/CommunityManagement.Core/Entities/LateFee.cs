namespace CommunityManagement.Core.Entities;

public class LateFee
{
    public Guid Id { get; set; }
    public Guid UnitDueId { get; set; }
    public decimal Amount { get; set; }
    public decimal Rate { get; set; }
    public int DaysOverdue { get; set; }
    public DateTimeOffset AppliedAt { get; set; }
    public Guid AppliedBy { get; set; }
    public string Status { get; set; } = default!;  // active|cancelled
    public DateTimeOffset? CancelledAt { get; set; }
    public Guid? CancelledBy { get; set; }
    public string? Note { get; set; }
}
