using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetBudgetVsActualQuery(
    Guid OrgId, int Year, int? Month
) : IRequest<BudgetVsActualResult>;

public record BudgetVsActualResult(
    IReadOnlyList<BudgetComparisonItem> Items,
    decimal TotalBudget, decimal TotalActual, decimal TotalDifference);

public record BudgetComparisonItem(
    Guid CategoryId, string CategoryName, string? CategoryIcon,
    decimal BudgetAmount, decimal ActualAmount,
    decimal Difference, decimal DifferencePercent,
    string Status);

public class GetBudgetVsActualQueryHandler : IRequestHandler<GetBudgetVsActualQuery, BudgetVsActualResult>
{
    private readonly IFinanceBudgetRepository _budgets;
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;

    public GetBudgetVsActualQueryHandler(
        IFinanceBudgetRepository budgets,
        IFinanceRecordRepository records,
        ICurrentUserService currentUser)
    {
        _budgets = budgets;
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<BudgetVsActualResult> Handle(GetBudgetVsActualQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        // Bütçe verileri
        var budgetItems = request.Month.HasValue
            ? await _budgets.GetByOrgMonthAsync(request.OrgId, request.Year, request.Month.Value, ct)
            : await _budgets.GetByOrgYearAsync(request.OrgId, request.Year, ct);

        // Gerçekleşen gider kırılımı
        IReadOnlyList<CategoryBreakdownItem> actuals;
        if (request.Month.HasValue)
        {
            actuals = await _records.GetCategoryBreakdownAsync(request.OrgId, "expense", request.Year, request.Month.Value, ct);
        }
        else
        {
            // Yıllık: tüm ayların toplamı
            var allMonths = new List<CategoryBreakdownItem>();
            for (int m = 1; m <= 12; m++)
            {
                var monthActuals = await _records.GetCategoryBreakdownAsync(request.OrgId, "expense", request.Year, m, ct);
                allMonths.AddRange(monthActuals);
            }
            actuals = allMonths
                .GroupBy(a => a.CategoryId)
                .Select(g => new CategoryBreakdownItem(
                    g.Key, g.First().CategoryName, g.First().CategoryIcon, g.First().ParentCategoryName,
                    g.Sum(x => x.Amount), 0))
                .ToList();
        }

        // Bütçe toplamı (aylık ise direkt, yıllık ise kategorileri grupla)
        var budgetByCategory = budgetItems
            .GroupBy(b => b.CategoryId)
            .ToDictionary(g => g.Key, g => (
                Name: g.First().CategoryName,
                Icon: g.First().CategoryIcon,
                Amount: g.Sum(x => x.Amount)));

        var actualByCategory = actuals.ToDictionary(a => a.CategoryId, a => a.Amount);

        var allCategoryIds = budgetByCategory.Keys.Union(actualByCategory.Keys).ToList();
        var items = new List<BudgetComparisonItem>();

        foreach (var catId in allCategoryIds)
        {
            budgetByCategory.TryGetValue(catId, out var budgetInfo);
            actualByCategory.TryGetValue(catId, out var actualAmount);

            var budgetAmount = budgetInfo.Amount;
            var catName = budgetInfo.Name ?? actuals.FirstOrDefault(a => a.CategoryId == catId)?.CategoryName ?? "";
            var catIcon = budgetInfo.Icon ?? actuals.FirstOrDefault(a => a.CategoryId == catId)?.CategoryIcon;
            var difference = budgetAmount - actualAmount;
            var diffPercent = budgetAmount > 0 ? Math.Round((actualAmount / budgetAmount) * 100, 1) : 0;

            var status = budgetAmount == 0 ? "no_budget"
                : actualAmount > budgetAmount ? "over_budget"
                : actualAmount >= budgetAmount * 0.8m ? "warning"
                : "under_budget";

            items.Add(new BudgetComparisonItem(
                catId, catName, catIcon,
                budgetAmount, actualAmount,
                difference, diffPercent, status));
        }

        return new BudgetVsActualResult(
            items.OrderByDescending(i => i.ActualAmount).ToList(),
            items.Sum(i => i.BudgetAmount),
            items.Sum(i => i.ActualAmount),
            items.Sum(i => i.Difference));
    }
}
