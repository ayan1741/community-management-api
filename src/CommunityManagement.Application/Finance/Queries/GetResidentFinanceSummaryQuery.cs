using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetResidentFinanceSummaryQuery(
    Guid OrgId, int Year, int Month,
    string ReportBasis = "period"
) : IRequest<ResidentFinanceSummaryResult>;

public record ResidentFinanceSummaryResult(
    int Year, int Month,
    decimal DuesCollected,
    decimal OtherIncome,
    decimal TotalIncome,
    decimal TotalExpense,
    decimal NetBalance,
    IReadOnlyList<CategoryBreakdownItem> ExpenseBreakdown,
    int ActiveUnitCount,
    decimal PerUnitShare,
    IReadOnlyList<MonthAmountItem> ExpenseTrend);

public class GetResidentFinanceSummaryQueryHandler : IRequestHandler<GetResidentFinanceSummaryQuery, ResidentFinanceSummaryResult>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public GetResidentFinanceSummaryQueryHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _records = records;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<ResidentFinanceSummaryResult> Handle(GetResidentFinanceSummaryQuery request, CancellationToken ct)
    {
        // Tüm authenticated üyeler görebilir (şeffaflık)
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        if (request.ReportBasis is not ("period" or "cash"))
            throw Application.Common.AppException.UnprocessableEntity("reportBasis 'period' veya 'cash' olmalıdır.");

        var basis = request.ReportBasis;

        var totals = await _records.GetMonthlyTotalsAsync(request.OrgId, request.Year, request.Month, basis, ct);
        var duesCollected = await _records.GetDuesCollectedAsync(request.OrgId, request.Year, request.Month, basis, ct);
        var expenseBreakdown = await _records.GetCategoryBreakdownAsync(request.OrgId, "expense", request.Year, request.Month, basis, ct);
        var expenseTrend = await _records.GetExpenseTrendAsync(request.OrgId, 6, basis, ct);

        // Aktif ünite sayısı
        using var conn = _factory.CreateUserConnection();
        var activeUnitCount = (int)await conn.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM public.units WHERE organization_id = @OrgId AND deleted_at IS NULL",
            new { OrgId = request.OrgId });

        var totalIncome = duesCollected + totals.TotalIncome;
        var netBalance = totalIncome - totals.TotalExpense;
        var perUnitShare = activeUnitCount > 0 ? Math.Round(totals.TotalExpense / activeUnitCount, 2) : 0;

        return new ResidentFinanceSummaryResult(
            request.Year, request.Month,
            duesCollected, totals.TotalIncome, totalIncome,
            totals.TotalExpense, netBalance,
            expenseBreakdown, activeUnitCount, perUnitShare,
            expenseTrend);
    }
}
