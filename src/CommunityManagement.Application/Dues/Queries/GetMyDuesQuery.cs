using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Dues.Queries;

public record GetMyDuesQuery(Guid OrgId) : IRequest<IReadOnlyList<UnitDueResidentItem>>;

public class GetMyDuesQueryHandler : IRequestHandler<GetMyDuesQuery, IReadOnlyList<UnitDueResidentItem>>
{
    private readonly IUnitDueRepository _unitDues;
    private readonly ICurrentUserService _currentUser;

    public GetMyDuesQueryHandler(IUnitDueRepository unitDues, ICurrentUserService currentUser)
    {
        _unitDues = unitDues;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<UnitDueResidentItem>> Handle(GetMyDuesQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        return await _unitDues.GetByUserIdAsync(_currentUser.UserId, request.OrgId, ct);
    }
}
