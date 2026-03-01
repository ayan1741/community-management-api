using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record DeactivateDueTypeCommand(Guid OrgId, Guid DueTypeId) : IRequest;

public class DeactivateDueTypeCommandHandler : IRequestHandler<DeactivateDueTypeCommand>
{
    private readonly IDueTypeRepository _dueTypes;
    private readonly ICurrentUserService _currentUser;

    public DeactivateDueTypeCommandHandler(IDueTypeRepository dueTypes, ICurrentUserService currentUser)
    {
        _dueTypes = dueTypes;
        _currentUser = currentUser;
    }

    public async Task Handle(DeactivateDueTypeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var dueType = await _dueTypes.GetByIdAsync(request.DueTypeId, ct)
            ?? throw AppException.NotFound("Aidat tipi bulunamadı.");

        if (dueType.OrganizationId != request.OrgId)
            throw AppException.NotFound("Aidat tipi bulunamadı.");

        if (!dueType.IsActive)
            throw AppException.UnprocessableEntity("Aidat tipi zaten pasif durumda.");

        dueType.IsActive = false;
        dueType.UpdatedAt = DateTimeOffset.UtcNow;
        await _dueTypes.UpdateAsync(dueType, ct);
    }
}
