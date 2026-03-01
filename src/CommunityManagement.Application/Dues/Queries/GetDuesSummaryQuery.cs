using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetDuesSummaryQuery(Guid OrgId) : IRequest<DuesSummaryDto>;

public record DuesSummaryDto(
    int ActivePeriods,
    long TotalPendingDues,
    decimal TotalPendingAmount,
    decimal TotalCollectedAmount,
    int OverdueDues
);

public class GetDuesSummaryQueryHandler : IRequestHandler<GetDuesSummaryQuery, DuesSummaryDto>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public GetDuesSummaryQueryHandler(IDuesPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<DuesSummaryDto> Handle(GetDuesSummaryQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var active = await _periods.GetByOrgIdAsync(request.OrgId, "active", ct);

        return new DuesSummaryDto(
            ActivePeriods: active.Count,
            TotalPendingDues: active.Sum(p => p.PendingCount),
            TotalPendingAmount: active.Sum(p => p.TotalAmount - p.CollectedAmount),
            TotalCollectedAmount: active.Sum(p => p.CollectedAmount),
            OverdueDues: 0  // Detaylı overdue sayısı Phase 3g'de dashboard ile eklenecek
        );
    }
}
