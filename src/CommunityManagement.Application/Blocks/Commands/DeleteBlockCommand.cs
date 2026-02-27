using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Blocks.Commands;

public record DeleteBlockCommand(Guid OrgId, Guid BlockId) : IRequest;

public class DeleteBlockCommandHandler : IRequestHandler<DeleteBlockCommand>
{
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public DeleteBlockCommandHandler(IBlockRepository blocks, ICurrentUserService currentUser)
    {
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task Handle(DeleteBlockCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var block = await _blocks.GetByIdAsync(request.BlockId, ct)
            ?? throw AppException.NotFound("Blok bulunamadı.");

        if (block.OrganizationId != request.OrgId)
            throw AppException.NotFound("Blok bulunamadı.");

        if (block.IsDefault)
            throw AppException.UnprocessableEntity("Varsayılan blok silinemez.");

        var unitCount = await _blocks.GetActiveUnitCountAsync(request.BlockId, ct);
        if (unitCount > 0)
            throw AppException.UnprocessableEntity($"Bu blokta {unitCount} aktif daire var. Silmeden önce daireleri kaldırın.");

        await _blocks.SoftDeleteAsync(request.BlockId, ct);
    }
}
