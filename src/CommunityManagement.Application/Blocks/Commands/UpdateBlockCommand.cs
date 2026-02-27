using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Blocks.Commands;

public record UpdateBlockCommand(Guid OrgId, Guid BlockId, string Name) : IRequest<Block>;

public class UpdateBlockCommandHandler : IRequestHandler<UpdateBlockCommand, Block>
{
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public UpdateBlockCommandHandler(IBlockRepository blocks, ICurrentUserService currentUser)
    {
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task<Block> Handle(UpdateBlockCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var block = await _blocks.GetByIdAsync(request.BlockId, ct)
            ?? throw AppException.NotFound("Blok bulunamadı.");

        if (block.OrganizationId != request.OrgId)
            throw AppException.NotFound("Blok bulunamadı.");

        var exists = await _blocks.ExistsByNameAsync(request.OrgId, request.Name, request.BlockId, ct);
        if (exists)
            throw AppException.UnprocessableEntity("Bu blok adı zaten kullanımda.");

        block.Name = request.Name;
        block.UpdatedAt = DateTimeOffset.UtcNow;

        await _blocks.UpdateAsync(block, ct);
        return block;
    }
}
