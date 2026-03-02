using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.UnitResidents.Queries;

public record GetUnitResidentsQuery(
    Guid OrgId,
    Guid UnitId
) : IRequest<IReadOnlyList<UnitResidentListItem>>;

public class GetUnitResidentsQueryHandler : IRequestHandler<GetUnitResidentsQuery, IReadOnlyList<UnitResidentListItem>>
{
    private readonly IUnitResidentRepository _unitResidents;
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public GetUnitResidentsQueryHandler(
        IUnitResidentRepository unitResidents,
        IUnitRepository units,
        ICurrentUserService currentUser)
    {
        _unitResidents = unitResidents;
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UnitResidentListItem>> Handle(GetUnitResidentsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var unit = await _units.GetByIdAsync(request.UnitId, ct)
            ?? throw AppException.NotFound("Daire bulunamadı.");
        if (unit.OrganizationId != request.OrgId)
            throw AppException.NotFound("Daire bulunamadı.");

        return await _unitResidents.GetByUnitIdAsync(request.UnitId, ct);
    }
}
