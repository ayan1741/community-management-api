using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.MaintenanceRequests.Queries;

public record GetMaintenanceRequestDetailQuery(Guid OrgId, Guid Id) : IRequest<GetMaintenanceRequestDetailResult>;

public record GetMaintenanceRequestDetailResult(
    MaintenanceRequestDetailDto Detail,
    IReadOnlyList<MaintenanceRequestLogItem> Timeline,
    IReadOnlyList<MaintenanceRequestCommentItem> Comments,
    IReadOnlyList<MaintenanceRequestCostItem>? Costs);

public class GetMaintenanceRequestDetailQueryHandler : IRequestHandler<GetMaintenanceRequestDetailQuery, GetMaintenanceRequestDetailResult>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;

    public GetMaintenanceRequestDetailQueryHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory, IMaintenanceRequestRepository repo)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
    }

    public async Task<GetMaintenanceRequestDetailResult> Handle(GetMaintenanceRequestDetailQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var detail = await _repo.GetDetailAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        var currentUserId = _currentUser.UserId;

        // Sakin gorunurluk kontrolu: sadece kendi bildirimi, ortak alan, veya kendi dairesi
        if (role == MemberRole.Resident)
        {
            if (detail.ReportedBy != currentUserId && detail.LocationType != "common_area")
            {
                // Kendi dairesi mi kontrol et
                if (detail.UnitId is null)
                    throw AppException.NotFound("Ariza bildirimi bulunamadi.");

                using var conn = _factory.CreateUserConnection();
                var isResident = await conn.QuerySingleAsync<long>(
                    """
                    SELECT COUNT(*) FROM public.unit_residents
                    WHERE unit_id = @UnitId AND user_id = @UserId AND status = 'active'
                    """,
                    new { UnitId = detail.UnitId, UserId = currentUserId });
                if (isResident == 0)
                    throw AppException.NotFound("Ariza bildirimi bulunamadi.");
            }
        }

        var timeline = await _repo.GetLogsAsync(request.Id, ct);
        var comments = await _repo.GetCommentsAsync(request.Id, ct);

        // Maliyet sadece admin/board_member gorebilir
        IReadOnlyList<MaintenanceRequestCostItem>? costs = null;
        if (role is MemberRole.Admin or MemberRole.BoardMember)
        {
            costs = await _repo.GetCostsAsync(request.Id, ct);
        }

        return new GetMaintenanceRequestDetailResult(detail, timeline, comments, costs);
    }
}
