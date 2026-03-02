using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.UnitResidents.Commands;

public record RemoveUnitResidentCommand(
    Guid OrgId,
    Guid UnitResidentId
) : IRequest;

public class RemoveUnitResidentCommandHandler : IRequestHandler<RemoveUnitResidentCommand>
{
    private readonly IUnitResidentRepository _unitResidents;
    private readonly ICurrentUserService _currentUser;

    public RemoveUnitResidentCommandHandler(
        IUnitResidentRepository unitResidents,
        ICurrentUserService currentUser)
    {
        _unitResidents = unitResidents;
        _currentUser = currentUser;
    }

    public async Task Handle(RemoveUnitResidentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var resident = await _unitResidents.GetByIdAsync(request.UnitResidentId, ct)
            ?? throw AppException.NotFound("Sakin-daire kaydı bulunamadı.");

        if (resident.OrganizationId != request.OrgId)
            throw AppException.NotFound("Sakin-daire kaydı bulunamadı.");

        await _unitResidents.RemoveAsync(request.UnitResidentId, _currentUser.UserId, ct);
    }
}
