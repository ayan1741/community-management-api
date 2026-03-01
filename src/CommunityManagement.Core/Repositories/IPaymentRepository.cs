using CommunityManagement.Core.Entities;
using System.Data;

namespace CommunityManagement.Core.Repositories;

public interface IPaymentRepository
{
    Task<IReadOnlyList<PaymentListItem>> GetByUnitDueIdAsync(Guid unitDueId, CancellationToken ct = default);
    Task<(IReadOnlyList<PaymentHistoryItem> Items, int TotalCount)> GetByResidentAsync(
        Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<decimal> GetTotalPaidAsync(Guid unitDueId, CancellationToken ct = default);
    Task<Payment> CreateAsync(Payment payment, CancellationToken ct = default);
    Task UpdateAsync(Payment payment, CancellationToken ct = default);
    // Fiziksel silme yok — soft-delete (cancelled_at / cancelled_by set eder)
    Task SoftDeleteAsync(Guid id, Guid cancelledBy, CancellationToken ct = default);
    // Tahakkuk iptali sırasında toplu soft-delete (transaction içinde)
    Task SoftDeleteByUnitDueIdAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default);
}

public record PaymentListItem(
    Guid Id,
    string ReceiptNumber,
    decimal Amount,
    DateTimeOffset PaidAt,
    string PaymentMethod,
    string? CollectedByName,
    bool IsOverpayment,
    decimal? OverpaymentAmount,
    string? Note,
    DateTimeOffset CreatedAt
);

public record PaymentHistoryItem(
    Guid Id,
    string ReceiptNumber,
    decimal Amount,
    DateTimeOffset PaidAt,
    string PaymentMethod,
    string? CollectedByName,
    string PeriodName,
    string DueTypeName,
    string UnitNumber,
    string BlockName,
    DateTimeOffset CreatedAt
);
