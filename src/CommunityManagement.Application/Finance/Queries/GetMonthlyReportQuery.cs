using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetMonthlyReportQuery(
    Guid OrgId, int Year, int Month
) : IRequest<MonthlyReportResult>;

public record MonthlyReportResult(
    int Year, int Month,
    decimal DuesCollected,
    decimal OtherIncome,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetBalance,
    decimal? PreviousMonthExpense,
    decimal? ChangePercent,
    IReadOnlyList<CategoryBreakdownItem> ExpenseBreakdown,
    IReadOnlyList<CategoryBreakdownItem> IncomeBreakdown,
    IReadOnlyList<FinanceRecordListItem> RecentRecords);

public class GetMonthlyReportQueryHandler : IRequestHandler<GetMonthlyReportQuery, MonthlyReportResult>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;

    public GetMonthlyReportQueryHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<MonthlyReportResult> Handle(GetMonthlyReportQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        // Sıralı sorgular (Npgsql aynı connection'da paralel query desteklemez)
        var totals = await _records.GetMonthlyTotalsAsync(request.OrgId, request.Year, request.Month, ct);
        var duesCollected = await _records.GetDuesCollectedAsync(request.OrgId, request.Year, request.Month, ct);
        var expenseBreakdown = await _records.GetCategoryBreakdownAsync(request.OrgId, "expense", request.Year, request.Month, ct);
        var incomeBreakdown = await _records.GetCategoryBreakdownAsync(request.OrgId, "income", request.Year, request.Month, ct);

        // Önceki ay gider (değişim hesabı)
        var prevMonth = request.Month == 1 ? 12 : request.Month - 1;
        var prevYear = request.Month == 1 ? request.Year - 1 : request.Year;
        var prevTotals = await _records.GetMonthlyTotalsAsync(request.OrgId, prevYear, prevMonth, ct);

        decimal? previousMonthExpense = prevTotals.TotalExpense > 0 ? prevTotals.TotalExpense : null;
        decimal? changePercent = null;
        if (previousMonthExpense.HasValue && previousMonthExpense.Value > 0)
            changePercent = Math.Round((totals.TotalExpense - previousMonthExpense.Value) / previousMonthExpense.Value * 100, 1);

        // Son 10 kayıt
        var (recentRecords, _) = await _records.GetByOrgIdAsync(
            request.OrgId, null, null, null, null, 1, 10, ct);

        var totalIncome = duesCollected + totals.TotalIncome;
        var netBalance = totalIncome - totals.TotalExpense;

        return new MonthlyReportResult(
            request.Year, request.Month,
            duesCollected, totals.TotalIncome, totalIncome,
            totals.TotalExpense, netBalance,
            previousMonthExpense, changePercent,
            expenseBreakdown, incomeBreakdown, recentRecords);
    }
}
