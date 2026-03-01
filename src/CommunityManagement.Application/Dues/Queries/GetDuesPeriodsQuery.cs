using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetDuesPeriodsQuery(Guid OrgId) : IRequest<IReadOnlyList<DuesPeriodListItem>>;

public class GetDuesPeriodsQueryHandler : IRequestHandler<GetDuesPeriodsQuery, IReadOnlyList<DuesPeriodListItem>>
{
    private readonly IDuesPeriodRepository _periods;
    private readonly ICurrentUserService _currentUser;

    public GetDuesPeriodsQueryHandler(IDuesPeriodRepository periods, ICurrentUserService currentUser)
    {
        _periods = periods;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DuesPeriodListItem>> Handle(GetDuesPeriodsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);
        return await _periods.GetByOrgIdAsync(request.OrgId, ct: ct);
    }
}
