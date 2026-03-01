using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Commands;

public record CreateDueTypeCommand(
    Guid OrgId,
    string Name,
    string? Description,
    decimal DefaultAmount,
    string? CategoryAmounts
) : IRequest<DueType>;

public class CreateDueTypeCommandHandler : IRequestHandler<CreateDueTypeCommand, DueType>
{
    private readonly IDueTypeRepository _dueTypes;
    private readonly ICurrentUserService _currentUser;

    public CreateDueTypeCommandHandler(IDueTypeRepository dueTypes, ICurrentUserService currentUser)
    {
        _dueTypes = dueTypes;
        _currentUser = currentUser;
    }

    public async Task<DueType> Handle(CreateDueTypeCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var exists = await _dueTypes.ExistsByNameAsync(request.OrgId, request.Name, null, ct);
        if (exists)
            throw AppException.Conflict("Bu isimde bir aidat tipi zaten mevcut.");

        var dueType = new DueType
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Name = request.Name.Trim(),
            Description = request.Description,
            DefaultAmount = request.DefaultAmount,
            CategoryAmounts = request.CategoryAmounts,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return await _dueTypes.CreateAsync(dueType, ct);
    }
}
