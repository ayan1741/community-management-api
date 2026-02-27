using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Units.Queries;

public record GetUnitDropdownQuery(Guid OrgId) : IRequest<IReadOnlyList<UnitDropdownItem>>;

public class GetUnitDropdownQueryHandler : IRequestHandler<GetUnitDropdownQuery, IReadOnlyList<UnitDropdownItem>>
{
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public GetUnitDropdownQueryHandler(IUnitRepository units, ICurrentUserService currentUser)
    {
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UnitDropdownItem>> Handle(GetUnitDropdownQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);
        return await _units.GetDropdownByOrgIdAsync(request.OrgId, ct);
    }
}
