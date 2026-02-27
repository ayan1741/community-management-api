using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using UnitEntity = CommunityManagement.Core.Entities.Unit;

namespace CommunityManagement.Application.Units.Commands;

public record UpdateUnitCommand(
    Guid OrgId,
    Guid UnitId,
    string UnitNumber,
    string UnitType,
    int? Floor,
    decimal? AreaSqm,
    string? Notes
) : IRequest<UnitEntity>;

public class UpdateUnitCommandHandler : IRequestHandler<UpdateUnitCommand, UnitEntity>
{
    private readonly IUnitRepository _units;
    private readonly ICurrentUserService _currentUser;

    public UpdateUnitCommandHandler(IUnitRepository units, ICurrentUserService currentUser)
    {
        _units = units;
        _currentUser = currentUser;
    }

    public async Task<UnitEntity> Handle(UpdateUnitCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var unit = await _units.GetByIdAsync(request.UnitId, ct)
            ?? throw AppException.NotFound("Daire bulunamadı.");

        if (unit.OrganizationId != request.OrgId)
            throw AppException.NotFound("Daire bulunamadı.");

        var exists = await _units.ExistsByNumberAsync(unit.BlockId, request.UnitNumber, request.UnitId, ct);
        if (exists)
            throw AppException.Conflict("Bu blokta aynı daire numarası zaten mevcut.");

        unit.UnitNumber = request.UnitNumber;
        unit.UnitType = request.UnitType;
        unit.Floor = request.Floor;
        unit.AreaSqm = request.AreaSqm;
        unit.Notes = request.Notes;
        unit.UpdatedAt = DateTimeOffset.UtcNow;

        await _units.UpdateAsync(unit, ct);
        return unit;
    }
}
