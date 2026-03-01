using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetDueTypesQuery(Guid OrgId, bool? IsActive) : IRequest<IReadOnlyList<DueType>>;

public class GetDueTypesQueryHandler : IRequestHandler<GetDueTypesQuery, IReadOnlyList<DueType>>
{
    private readonly IDueTypeRepository _dueTypes;
    private readonly ICurrentUserService _currentUser;

    public GetDueTypesQueryHandler(IDueTypeRepository dueTypes, ICurrentUserService currentUser)
    {
        _dueTypes = dueTypes;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<DueType>> Handle(GetDueTypesQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        return await _dueTypes.GetByOrgIdAsync(request.OrgId, request.IsActive, ct);
    }
}
