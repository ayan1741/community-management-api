using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.MaintenanceRequests.Queries;

public record GetMaintenanceRequestsQuery(
    Guid OrgId,
    string? Status, string? Category, string? Priority, string? LocationType,
    bool? IsRecurring, bool? SlaBreached,
    int Page, int PageSize
) : IRequest<GetMaintenanceRequestsResult>;

public record GetMaintenanceRequestsResult(
    IReadOnlyList<MaintenanceRequestListItem> Items, int TotalCount,
    int Page, int PageSize);

public class GetMaintenanceRequestsQueryHandler : IRequestHandler<GetMaintenanceRequestsQuery, GetMaintenanceRequestsResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public GetMaintenanceRequestsQueryHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<GetMaintenanceRequestsResult> Handle(GetMaintenanceRequestsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        var roleStr = role switch
        {
            MemberRole.Admin => "admin",
            MemberRole.BoardMember => "board_member",
            _ => "resident"
        };

        // Sakin'in dairelerini al
        Guid[]? unitIds = null;
        if (role == MemberRole.Resident)
        {
            using var conn = _factory.CreateUserConnection();
            var ids = await conn.QueryAsync<Guid>(
                """
                SELECT ur.unit_id FROM public.unit_residents ur
                WHERE ur.user_id = @UserId AND ur.status = 'active'
                """,
                new { UserId = currentUserId });
            unitIds = ids.ToArray();
        }

        var (items, totalCount) = await _repo.GetListAsync(
            request.OrgId, currentUserId, roleStr, unitIds,
            request.Status, request.Category, request.Priority, request.LocationType,
            request.IsRecurring, request.SlaBreached,
            request.Page, request.PageSize, ct);

        return new GetMaintenanceRequestsResult(items, totalCount, request.Page, request.PageSize);
    }
}
