using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Blocks.Queries;

public record GetBlocksQuery(Guid OrgId) : IRequest<IReadOnlyList<BlockListItem>>;

public class GetBlocksQueryHandler : IRequestHandler<GetBlocksQuery, IReadOnlyList<BlockListItem>>
{
    private readonly IBlockRepository _blocks;
    private readonly ICurrentUserService _currentUser;

    public GetBlocksQueryHandler(IBlockRepository blocks, ICurrentUserService currentUser)
    {
        _blocks = blocks;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<BlockListItem>> Handle(GetBlocksQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        return await _blocks.GetByOrgIdAsync(request.OrgId, ct);
    }
}
