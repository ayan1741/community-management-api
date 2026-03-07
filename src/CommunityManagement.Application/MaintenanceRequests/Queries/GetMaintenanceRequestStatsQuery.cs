using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.MaintenanceRequests.Queries;

public record GetMaintenanceRequestStatsQuery(Guid OrgId) : IRequest<MaintenanceRequestStats>;

public class GetMaintenanceRequestStatsQueryHandler : IRequestHandler<GetMaintenanceRequestStatsQuery, MaintenanceRequestStats>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IMaintenanceRequestRepository _repo;

    public GetMaintenanceRequestStatsQueryHandler(
        ICurrentUserService currentUser, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _repo = repo;
    }

    public async Task<MaintenanceRequestStats> Handle(GetMaintenanceRequestStatsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);
        return await _repo.GetStatsAsync(request.OrgId, ct);
    }
}
