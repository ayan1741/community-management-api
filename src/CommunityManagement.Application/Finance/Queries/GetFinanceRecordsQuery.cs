using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Finance.Queries;

public record GetFinanceRecordsQuery(
    Guid OrgId, string? Type, Guid? CategoryId,
    DateOnly? StartDate, DateOnly? EndDate,
    int? PeriodYear, int? PeriodMonth,
    int Page, int PageSize
) : IRequest<FinanceRecordListResult>;

public record FinanceRecordListResult(
    IReadOnlyList<FinanceRecordListItem> Items, int TotalCount, int Page, int PageSize);

public class GetFinanceRecordsQueryHandler : IRequestHandler<GetFinanceRecordsQuery, FinanceRecordListResult>
{
    private readonly IFinanceRecordRepository _records;
    private readonly ICurrentUserService _currentUser;

    public GetFinanceRecordsQueryHandler(
        IFinanceRecordRepository records,
        ICurrentUserService currentUser)
    {
        _records = records;
        _currentUser = currentUser;
    }

    public async Task<FinanceRecordListResult> Handle(GetFinanceRecordsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var (items, totalCount) = await _records.GetByOrgIdAsync(
            request.OrgId, request.Type, request.CategoryId,
            request.StartDate, request.EndDate,
            request.PeriodYear, request.PeriodMonth,
            request.Page, request.PageSize, ct);

        return new FinanceRecordListResult(items, totalCount, request.Page, request.PageSize);
    }
}
