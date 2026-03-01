using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record UpdateDueTypeCommand(
    Guid OrgId,
    Guid DueTypeId,
    string Name,
    string? Description,
    decimal DefaultAmount,
    string? CategoryAmounts
) : IRequest<DueType>;

public class UpdateDueTypeCommandHandler : IRequestHandler<UpdateDueTypeCommand, DueType>
{
    private readonly IDueTypeRepository _dueTypes;
    private readonly ICurrentUserService _currentUser;

    public UpdateDueTypeCommandHandler(IDueTypeRepository dueTypes, ICurrentUserService currentUser)
    {
        _dueTypes = dueTypes;
        _currentUser = currentUser;
    }

    public async Task<DueType> Handle(UpdateDueTypeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var dueType = await _dueTypes.GetByIdAsync(request.DueTypeId, ct)
            ?? throw AppException.NotFound("Aidat tipi bulunamadı.");

        if (dueType.OrganizationId != request.OrgId)
            throw AppException.NotFound("Aidat tipi bulunamadı.");

        var exists = await _dueTypes.ExistsByNameAsync(request.OrgId, request.Name, request.DueTypeId, ct);
        if (exists)
            throw AppException.Conflict("Bu isimde bir aidat tipi zaten mevcut.");

        dueType.Name = request.Name.Trim();
        dueType.Description = request.Description;
        dueType.DefaultAmount = request.DefaultAmount;
        dueType.CategoryAmounts = request.CategoryAmounts;
        dueType.UpdatedAt = DateTimeOffset.UtcNow;

        await _dueTypes.UpdateAsync(dueType, ct);
        return dueType;
    }
}
