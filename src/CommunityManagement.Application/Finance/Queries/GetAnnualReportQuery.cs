using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetAnnualReportQuery(
    Guid OrgId, int Year,
    string ReportBasis = "period"
) : IRequest<AnnualReportResult>;

public record AnnualReportResult(
    int Year,
    IReadOnlyList<AnnualMonthRow> MonthlyTotals,
    decimal YearTotalIncome, decimal YearTotalExpense, decimal YearNetBalance,
    decimal YearDuesCollected);

public record AnnualMonthRow(
    int Year, int Month,
    decimal DuesCollected, decimal OtherIncome,
    decimal TotalIncome, decimal TotalExpense, decimal NetBalance);

public class GetAnnualReportQueryHandler : IRequestHandler<GetAnnualReportQuery, AnnualReportResult>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;

    public GetAnnualReportQueryHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<AnnualReportResult> Handle(GetAnnualReportQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        if (request.ReportBasis is not ("period" or "cash"))
            throw Application.Common.AppException.UnprocessableEntity("reportBasis 'period' veya 'cash' olmalıdır.");

        var basis = request.ReportBasis;

        var financeTotals = await _records.GetAnnualTotalsAsync(request.OrgId, request.Year, basis, ct);
        var duesCollected = await _records.GetAnnualDuesCollectedAsync(request.OrgId, request.Year, basis, ct);

        // 12 aylık tablo oluştur
        var financeByMonth = financeTotals.ToDictionary(t => t.Month);
        var monthlyRows = new List<AnnualMonthRow>();

        for (int m = 1; m <= 12; m++)
        {
            financeByMonth.TryGetValue(m, out var ft);
            var otherIncome = ft?.TotalIncome ?? 0;
            var expense = ft?.TotalExpense ?? 0;
            var dues = duesCollected[m - 1];
            var totalIncome = dues + otherIncome;

            monthlyRows.Add(new AnnualMonthRow(
                request.Year, m, dues, otherIncome, totalIncome, expense, totalIncome - expense));
        }

        var yearDues = monthlyRows.Sum(r => r.DuesCollected);
        var yearIncome = monthlyRows.Sum(r => r.TotalIncome);
        var yearExpense = monthlyRows.Sum(r => r.TotalExpense);

        return new AnnualReportResult(
            request.Year, monthlyRows,
            yearIncome, yearExpense, yearIncome - yearExpense, yearDues);
    }
}
