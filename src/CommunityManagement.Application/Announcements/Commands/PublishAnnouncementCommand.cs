using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record PublishAnnouncementCommand(Guid OrgId, Guid AnnouncementId) : IRequest<PublishAnnouncementResult>;

public record PublishAnnouncementResult(
    DateTimeOffset PublishedAt, int TargetMemberCount, int NotificationCount);

public class PublishAnnouncementCommandHandler : IRequestHandler<PublishAnnouncementCommand, PublishAnnouncementResult>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public PublishAnnouncementCommandHandler(
        IAnnouncementRepository announcements, ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _announcements = announcements;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<PublishAnnouncementResult> Handle(PublishAnnouncementCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.Status != "draft")
            throw AppException.Conflict("Bu duyuru zaten yayınlanmış veya süresi dolmuş.");

        // Board member sadece kendi duyurusunu yayınlayabilir
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        if (role == MemberRole.BoardMember && existing.CreatedBy != _currentUser.UserId)
            throw AppException.Forbidden("Sadece kendi oluşturduğunuz duyuruyu yayınlayabilirsiniz.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // 1. Hedef kitleyi hesapla
            var targetMembers = await CalculateTargetMembersAsync(conn, tx, existing, ct);
            var targetMemberCount = targetMembers.Count;

            // 2. Duyuruyu yayınla
            await conn.ExecuteAsync(
                """
                UPDATE public.announcements
                SET status = 'published', published_at = @PublishedAt, target_member_count = @TargetMemberCount,
                    updated_by = @UpdatedBy
                WHERE id = @Id AND status = 'draft'
                """,
                new
                {
                    Id = request.AnnouncementId,
                    PublishedAt = now.UtcDateTime,
                    TargetMemberCount = targetMemberCount,
                    UpdatedBy = currentUserId
                }, tx);

            // 3. Bildirim oluştur (batch INSERT) — yayınlayan kullanıcıyı hariç tut
            var userIds = targetMembers
                .Where(m => m.UserId != currentUserId)
                .Select(m => m.UserId)
                .ToArray();
            var notificationCount = userIds.Length;
            if (notificationCount > 0)
            {
                var truncatedBody = existing.Body.Length > 200 ? existing.Body[..200] + "..." : existing.Body;

                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.notifications
                        (organization_id, user_id, type, title, body, reference_type, reference_id)
                    SELECT @OrgId, unnest(@UserIds), 'announcement', @Title, @Body, 'announcement', @ReferenceId
                    """,
                    new
                    {
                        OrgId = request.OrgId,
                        UserIds = userIds,
                        Title = existing.Title,
                        Body = truncatedBody,
                        ReferenceId = existing.Id
                    }, tx);
            }

            // 4. Email job oluştur
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status)
                VALUES ('announcement_email', @Payload::jsonb, 'queued')
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        announcement_id = existing.Id,
                        organization_id = request.OrgId
                    })
                }, tx);

            // 5. Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('announcements', @RecordId, @ActorId, 'publish', @NewValues::jsonb)
                """,
                new
                {
                    RecordId = existing.Id,
                    ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new
                    {
                        existing.Title, TargetMemberCount = targetMemberCount, NotificationCount = notificationCount
                    })
                }, tx);

            await tx.CommitAsync(ct);
            return new PublishAnnouncementResult(now, targetMemberCount, notificationCount);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static async Task<List<TargetMember>> CalculateTargetMembersAsync(
        System.Data.Common.DbConnection conn, System.Data.Common.DbTransaction tx,
        Core.Entities.Announcement announcement, CancellationToken ct)
    {
        string sql;
        object param;

        switch (announcement.TargetType)
        {
            case "block":
                var blockIds = JsonSerializer.Deserialize<List<string>>(announcement.TargetIds!)!;
                sql = """
                    SELECT DISTINCT om.user_id
                    FROM public.organization_members om
                    JOIN public.unit_residents ur ON ur.user_id = om.user_id AND ur.status = 'active'
                    JOIN public.units u ON u.id = ur.unit_id
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                      AND u.block_id = ANY(@BlockIds::uuid[])
                    """;
                param = new { OrgId = announcement.OrganizationId, BlockIds = blockIds.ToArray() };
                break;

            case "role":
                var roles = JsonSerializer.Deserialize<List<string>>(announcement.TargetIds!)!;
                sql = """
                    SELECT DISTINCT om.user_id
                    FROM public.organization_members om
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                      AND om.role = ANY(@Roles)
                    """;
                param = new { OrgId = announcement.OrganizationId, Roles = roles.ToArray() };
                break;

            default: // "all"
                sql = """
                    SELECT DISTINCT om.user_id
                    FROM public.organization_members om
                    WHERE om.organization_id = @OrgId AND om.status = 'active'
                    """;
                param = new { OrgId = announcement.OrganizationId };
                break;
        }

        var rows = await conn.QueryAsync<TargetMember>(sql, param, tx);
        return rows.ToList();
    }

    private record TargetMember(Guid UserId);
}
