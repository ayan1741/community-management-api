using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Announcements.Queries;

public record GetAnnouncementDetailQuery(Guid OrgId, Guid AnnouncementId) : IRequest<AnnouncementDetail>;

public class GetAnnouncementDetailQueryHandler : IRequestHandler<GetAnnouncementDetailQuery, AnnouncementDetail>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public GetAnnouncementDetailQueryHandler(
        IAnnouncementRepository announcements, ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _announcements = announcements;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<AnnouncementDetail> Handle(GetAnnouncementDetailQuery request, CancellationToken ct)
    {
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        var currentUserId = _currentUser.UserId;

        var detail = await _announcements.GetDetailAsync(request.AnnouncementId, currentUserId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (detail.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        // Sakin sadece yayınlanmış (ve expire olmamış) duyuruları görebilir
        if (role == MemberRole.Resident)
        {
            if (detail.Status != "published")
                throw AppException.NotFound("Duyuru bulunamadı.");
            if (detail.ExpiresAt.HasValue && detail.ExpiresAt.Value < DateTimeOffset.UtcNow)
                throw AppException.NotFound("Duyuru bulunamadı.");

            // Hedef kitle kontrolü — sakin hedef kitlede mi?
            if (detail.TargetType == "block" && detail.TargetIds is not null)
            {
                var blockIds = System.Text.Json.JsonSerializer.Deserialize<List<string>>(detail.TargetIds)!;
                await using var checkConn = _factory.CreateServiceRoleDbConnection();
                await checkConn.OpenAsync(ct);
                var inTarget = await checkConn.ExecuteScalarAsync<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM public.unit_residents ur
                        JOIN public.units u ON u.id = ur.unit_id
                        WHERE ur.user_id = @UserId AND ur.status = 'active'
                        AND u.block_id = ANY(@BlockIds::uuid[])
                    )
                    """,
                    new { UserId = currentUserId, BlockIds = blockIds.ToArray() });
                if (!inTarget)
                    throw AppException.NotFound("Duyuru bulunamadı.");
            }
            else if (detail.TargetType == "role" && detail.TargetIds is not null)
            {
                var roles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(detail.TargetIds)!;
                await using var checkConn = _factory.CreateServiceRoleDbConnection();
                await checkConn.OpenAsync(ct);
                var inTarget = await checkConn.ExecuteScalarAsync<bool>(
                    """
                    SELECT EXISTS (
                        SELECT 1 FROM public.organization_members om2
                        WHERE om2.user_id = @UserId AND om2.organization_id = @OrgId
                        AND om2.status = 'active' AND om2.role = ANY(@Roles)
                    )
                    """,
                    new { UserId = currentUserId, OrgId = request.OrgId, Roles = roles.ToArray() });
                if (!inTarget)
                    throw AppException.NotFound("Duyuru bulunamadı.");
            }
        }

        // Otomatik okundu işaretleme (yayınlanmış duyurular için)
        if (detail.Status == "published" && !detail.IsRead)
        {
            try
            {
                await using var conn = _factory.CreateServiceRoleDbConnection();
                await conn.OpenAsync(ct);
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.announcement_reads (announcement_id, user_id)
                    VALUES (@AnnouncementId, @UserId)
                    ON CONFLICT (announcement_id, user_id) DO NOTHING
                    """,
                    new { AnnouncementId = request.AnnouncementId, UserId = currentUserId });
            }
            catch
            {
                // Okundu kaydı başarısız olursa duyuru detayını göstermekten vazgeçme
            }
        }

        return detail;
    }
}
