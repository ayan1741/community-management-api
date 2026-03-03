namespace CommunityManagement.Core.Entities;

public class FinanceRecord
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid CategoryId { get; set; }
    public string Type { get; set; } = default!; // "income" | "expense"
    public decimal Amount { get; set; }
    public DateOnly RecordDate { get; set; }
    public int PeriodYear { get; set; }
    public int PeriodMonth { get; set; }
    public string Description { get; set; } = default!;
    public string? PaymentMethod { get; set; }
    public string? DocumentUrl { get; set; }
    public bool IsOpeningBalance { get; set; }
    public Guid CreatedBy { get; set; }
    public Guid? UpdatedBy { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
