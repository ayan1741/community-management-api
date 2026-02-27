using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Units.Commands;

public record DeleteUnitCommand(Guid OrgId, Guid UnitId) : IRequest;

public class DeleteUnitCommandHandler : IRequestHandler<DeleteUnitCommand>
{
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public DeleteUnitCommandHandler(IUnitRepository units, ICurrentUserService currentUser)
    {
        _units = units;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteUnitCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var unit = await _units.GetByIdAsync(request.UnitId, ct)
            ?? throw AppException.NotFound("Daire bulunamadı.");

        if (unit.OrganizationId != request.OrgId)
            throw AppException.NotFound("Daire bulunamadı.");

        var hasResident = await _units.HasActiveResidentAsync(request.UnitId, ct);
        if (hasResident)
            throw AppException.Conflict("Bu dairede aktif sakin var.");

        await _units.SoftDeleteAsync(request.UnitId, ct);
    }
}
