using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetDuesPeriodDetailQuery(
    Guid OrgId,
    Guid PeriodId,
    string? Status,
    int Page,
    int PageSize
) : IRequest<DuesPeriodDetailResult>;

public record DuesPeriodDetailResult(
    DuesPeriod Period,
    IReadOnlyList<UnitDueListItem> Items,
    int TotalCount
);

public class GetDuesPeriodDetailQueryHandler : IRequestHandler<GetDuesPeriodDetailQuery, DuesPeriodDetailResult>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly IUnitDueRepository _unitDues;
    private readonly ICurrentUserService _currentUser;

    public GetDuesPeriodDetailQueryHandler(
        IDuesPeriodRepository periods,
        IUnitDueRepository unitDues,
        ICurrentUserService currentUser)
    {
        _periods = periods;
        _unitDues = unitDues;
        _currentUser = currentUser;
    }

    public async Task<DuesPeriodDetailResult> Handle(GetDuesPeriodDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var period = await _periods.GetByIdAsync(request.PeriodId, ct)
            ?? throw AppException.NotFound("Dönem bulunamadı.");

        if (period.OrganizationId != request.OrgId)
            throw AppException.NotFound("Dönem bulunamadı.");

        var (items, totalCount) = await _unitDues.GetByPeriodIdAsync(
            request.PeriodId, request.Status, request.Page, request.PageSize, ct);

        return new DuesPeriodDetailResult(period, items, totalCount);
    }
}
