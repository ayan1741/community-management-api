using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.UnitResidents.Commands;

public record AssignUnitResidentCommand(
    Guid OrgId,
    Guid UnitId,
    Guid UserId,
    ResidentType ResidentType
) : IRequest<AssignUnitResidentResult>;

public record AssignUnitResidentResult(Guid Id);

public class AssignUnitResidentCommandHandler : IRequestHandler<AssignUnitResidentCommand, AssignUnitResidentResult>
{
    private readonly IUnitResidentRepository _unitResidents;
    private readonly IUnitRepository _units;
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public AssignUnitResidentCommandHandler(
        IUnitResidentRepository unitResidents,
        IUnitRepository units,
        IMemberRepository members,
        ICurrentUserService currentUser)
    {
        _unitResidents = unitResidents;
        _units = units;
        _members = members;
        _currentUser = currentUser;
    }

    public async Task<AssignUnitResidentResult> Handle(AssignUnitResidentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        if (request.UserId == Guid.Empty)
            throw AppException.UnprocessableEntity("Kullanıcı ID gereklidir.");
        if (request.ResidentType == ResidentType.Unspecified)
            throw AppException.UnprocessableEntity("Sakin tipi seçilmelidir (Malik, Kiracı veya Diğer Sakin).");

        var unit = await _units.GetByIdAsync(request.UnitId, ct)
            ?? throw AppException.NotFound("Daire bulunamadı.");

        if (unit.OrganizationId != request.OrgId)
            throw AppException.NotFound("Daire bulunamadı.");

        var member = await _members.GetByUserIdAsync(request.OrgId, request.UserId, ct)
            ?? throw AppException.NotFound("Bu kullanıcı organizasyonda aktif üye değil.");

        var exists = await _unitResidents.ExistsActiveAsync(request.UnitId, request.UserId, ct);
        if (exists)
            throw AppException.UnprocessableEntity("Bu kullanıcı zaten bu daireye atanmış.");

        // İlk atanan sakin primary olur
        var currentResidents = await _unitResidents.GetByUnitIdAsync(request.UnitId, ct);
        var isPrimary = currentResidents.Count == 0;

        var resident = new UnitResident
        {
            Id = Guid.NewGuid(),
            UnitId = request.UnitId,
            UserId = request.UserId,
            OrganizationId = request.OrgId,
            ResidentType = request.ResidentType,
            IsPrimary = isPrimary,
            Status = UnitResidentStatus.Active
        };

        var created = await _unitResidents.CreateAsync(resident, ct);
        return new AssignUnitResidentResult(created?.Id ?? resident.Id);
    }
}
