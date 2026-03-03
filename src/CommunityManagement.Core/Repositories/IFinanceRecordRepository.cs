using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public record FinanceRecordListItem(
    Guid Id, Guid CategoryId, string CategoryName, string? CategoryIcon,
    string Type, decimal Amount, DateOnly RecordDate,
    int PeriodYear, int PeriodMonth,
    string Description, string? PaymentMethod, string? DocumentUrl,
    bool IsOpeningBalance, string CreatedByName,
    DateTimeOffset CreatedAt, int TotalCount);

public record MonthlyFinanceTotals(
    int Year, int Month,
    decimal TotalIncome, decimal TotalExpense, decimal NetBalance);

public record CategoryBreakdownItem(
    Guid CategoryId, string CategoryName, string? CategoryIcon, string? ParentCategoryName,
    decimal Amount, decimal Percentage);

public record MonthAmountItem(int Year, int Month, decimal Amount);

public interface IFinanceRecordRepository
{
    Task<FinanceRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<(IReadOnlyList<FinanceRecordListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, string? type, Guid? categoryId,
        DateOnly? startDate, DateOnly? endDate,
        int? periodYear, int? periodMonth,
        int page, int pageSize, CancellationToken ct = default);
    Task<MonthlyFinanceTotals> GetMonthlyTotalsAsync(Guid orgId, int year, int month, string reportBasis, CancellationToken ct = default);
    Task<IReadOnlyList<MonthlyFinanceTotals>> GetAnnualTotalsAsync(Guid orgId, int year, string reportBasis, CancellationToken ct = default);
    Task<IReadOnlyList<CategoryBreakdownItem>> GetCategoryBreakdownAsync(Guid orgId, string type, int year, int month, string reportBasis, CancellationToken ct = default);
    Task<IReadOnlyList<MonthAmountItem>> GetExpenseTrendAsync(Guid orgId, int months, string reportBasis, CancellationToken ct = default);
    Task<FinanceRecord?> GetOpeningBalanceAsync(Guid orgId, CancellationToken ct = default);
    Task<bool> HasOpeningBalanceAsync(Guid orgId, CancellationToken ct = default);
    Task<decimal> GetDuesCollectedAsync(Guid orgId, int year, int month, string reportBasis, CancellationToken ct = default);
    Task<IReadOnlyList<decimal>> GetAnnualDuesCollectedAsync(Guid orgId, int year, string reportBasis, CancellationToken ct = default);
    Task<FinanceRecord> CreateAsync(FinanceRecord record, CancellationToken ct = default);
    Task UpdateAsync(FinanceRecord record, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default);
}
