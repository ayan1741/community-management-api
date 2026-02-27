using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using UnitEntity = CommunityManagement.Core.Entities.Unit;

namespace CommunityManagement.Application.Units.Commands;

public record CreateUnitCommand(
    Guid OrgId,
    Guid BlockId,
    string UnitNumber,
    string UnitType,
    int? Floor,
    decimal? AreaSqm,
    string? Notes
) : IRequest<UnitEntity>;

public class CreateUnitCommandHandler : IRequestHandler<CreateUnitCommand, UnitEntity>
{
    private readonly IUnitRepository _units;
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public CreateUnitCommandHandler(
        IUnitRepository units,
        IBlockRepository blocks,
        ICurrentUserService currentUser)
    {
        _units = units;
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task<UnitEntity> Handle(CreateUnitCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var block = await _blocks.GetByIdAsync(request.BlockId, ct)
            ?? throw AppException.NotFound("Blok bulunamadı.");

        if (block.OrganizationId != request.OrgId)
            throw AppException.NotFound("Blok bulunamadı.");

        var exists = await _units.ExistsByNumberAsync(request.BlockId, request.UnitNumber, null, ct);
        if (exists)
            throw AppException.Conflict("Bu blokta aynı daire numarası zaten mevcut.");

        var unit = new UnitEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            BlockId = request.BlockId,
            UnitNumber = request.UnitNumber,
            UnitType = request.UnitType,
            Floor = request.Floor,
            AreaSqm = request.AreaSqm,
            Notes = request.Notes,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return await _units.CreateAsync(unit, ct);
    }
}
