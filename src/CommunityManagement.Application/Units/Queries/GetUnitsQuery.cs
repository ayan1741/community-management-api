using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Units.Queries;

public record GetUnitsQuery(
    Guid OrgId,
    Guid? BlockId,
    string? UnitType,
    bool? IsOccupied,
    string? Search,
    int Page,
    int PageSize
) : IRequest<GetUnitsResult>;

public record GetUnitsResult(
    IReadOnlyList<UnitListItem> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public class GetUnitsQueryHandler : IRequestHandler<GetUnitsQuery, GetUnitsResult>
{
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public GetUnitsQueryHandler(IUnitRepository units, ICurrentUserService currentUser)
    {
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<GetUnitsResult> Handle(GetUnitsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var (items, totalCount) = await _units.GetByOrgIdAsync(
            request.OrgId, request.BlockId, request.UnitType, request.IsOccupied,
            request.Search, request.Page, request.PageSize, ct);

        return new GetUnitsResult(items, totalCount, request.Page, request.PageSize);
    }
}
