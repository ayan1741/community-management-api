using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public record BudgetWithCategoryItem(
    Guid Id, Guid CategoryId, string CategoryName, string? CategoryIcon,
    int Year, int Month, decimal Amount);

public interface IFinanceBudgetRepository
{
    Task<IReadOnlyList<BudgetWithCategoryItem>> GetByOrgMonthAsync(Guid orgId, int year, int month, CancellationToken ct = default);
    Task<IReadOnlyList<BudgetWithCategoryItem>> GetByOrgYearAsync(Guid orgId, int year, CancellationToken ct = default);
    Task<FinanceBudget> UpsertAsync(FinanceBudget budget, CancellationToken ct = default);
    Task<int> CopyMonthAsync(Guid orgId, int fromYear, int fromMonth, int toYear, int toMonth, Guid createdBy, CancellationToken ct = default);
}
