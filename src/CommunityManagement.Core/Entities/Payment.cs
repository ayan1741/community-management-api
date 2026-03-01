namespace CommunityManagement.Core.Entities;

public class Payment
{
    public Guid Id { get; set; }
    public Guid UnitDueId { get; set; }
    public string ReceiptNumber { get; set; } = default!;
    public decimal Amount { get; set; }
    public DateTimeOffset PaidAt { get; set; }
    public string PaymentMethod { get; set; } = default!;  // cash|bank_transfer|other
    public Guid? CollectedBy { get; set; }
    public bool IsOverpayment { get; set; }
    public decimal? OverpaymentAmount { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
