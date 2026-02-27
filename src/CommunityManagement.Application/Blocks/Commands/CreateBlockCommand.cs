using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Blocks.Commands;

public record CreateBlockCommand(Guid OrgId, string Name) : IRequest<Block>;

public class CreateBlockCommandHandler : IRequestHandler<CreateBlockCommand, Block>
{
    private readonly IBlockRepository _blocks;
    private readonly IOrganizationRepository _organizations;
    private readonly ICurrentUserService _currentUser;

    public CreateBlockCommandHandler(
        IBlockRepository blocks,
        IOrganizationRepository organizations,
        ICurrentUserService currentUser)
    {
        _blocks = blocks;
        _organizations = organizations;
        _currentUser = currentUser;
    }

    public async Task<Block> Handle(CreateBlockCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var org = await _organizations.GetByIdAsync(request.OrgId, ct)
            ?? throw AppException.NotFound("Organizasyon bulunamadı.");

        if (org.OrgType == "apartment")
            throw AppException.UnprocessableEntity("Apartman tipinde birden fazla blok eklenemez.");

        var exists = await _blocks.ExistsByNameAsync(request.OrgId, request.Name, null, ct);
        if (exists)
            throw AppException.UnprocessableEntity("Bu blok adı zaten kullanımda.");

        var block = new Block
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Name = request.Name,
            BlockType = "residential",
            IsDefault = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        return await _blocks.CreateAsync(block, ct);
    }
}
