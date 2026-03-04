using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record UpdateAnnouncementCommand(
    Guid OrgId, Guid AnnouncementId,
    string Title, string Body,
    string Category, string Priority,
    string TargetType, List<string>? TargetIds,
    DateTimeOffset? ExpiresAt
) : IRequest<Announcement>;

public class UpdateAnnouncementCommandHandler : IRequestHandler<UpdateAnnouncementCommand, Announcement>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public UpdateAnnouncementCommandHandler(
        IAnnouncementRepository announcements, ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _announcements = announcements;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<Announcement> Handle(UpdateAnnouncementCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.Status != "draft")
            throw AppException.Forbidden("Sadece taslak duyurular düzenlenebilir.");

        // Board member sadece kendi duyurusunu düzenleyebilir
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        if (role == MemberRole.BoardMember && existing.CreatedBy != _currentUser.UserId)
            throw AppException.Forbidden("Sadece kendi oluşturduğunuz duyuruyu düzenleyebilirsiniz.");

        // Validasyonlar
        if (string.IsNullOrWhiteSpace(request.Title))
            throw AppException.UnprocessableEntity("Başlık zorunludur.");
        if (request.Title.Trim().Length > 200)
            throw AppException.UnprocessableEntity("Başlık en fazla 200 karakter olabilir.");
        if (string.IsNullOrWhiteSpace(request.Body))
            throw AppException.UnprocessableEntity("Duyuru içeriği zorunludur.");
        if (request.Category is not ("general" or "urgent" or "maintenance" or "meeting" or "financial" or "other"))
            throw AppException.UnprocessableEntity("Geçersiz kategori.");
        if (request.Priority is not ("normal" or "important" or "urgent"))
            throw AppException.UnprocessableEntity("Geçersiz öncelik.");
        if (request.TargetType is not ("all" or "block" or "role"))
            throw AppException.UnprocessableEntity("Geçersiz hedef kitle tipi.");
        if (request.TargetType != "all" && (request.TargetIds is null || request.TargetIds.Count == 0))
            throw AppException.UnprocessableEntity("Hedef kitle seçimi yapılmalıdır.");

        // Blok doğrulama
        if (request.TargetType == "block" && request.TargetIds is not null)
        {
            using var checkConn = _factory.CreateUserConnection();
            var blockCount = await checkConn.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM public.blocks WHERE organization_id = @OrgId AND id = ANY(@BlockIds::uuid[]) AND deleted_at IS NULL",
                new { request.OrgId, BlockIds = request.TargetIds.ToArray() });

            if ((int)blockCount != request.TargetIds.Count)
                throw AppException.NotFound("Bir veya daha fazla blok bulunamadı.");
        }

        // Rol doğrulama
        if (request.TargetType == "role" && request.TargetIds is not null)
        {
            var validRoles = new HashSet<string> { "admin", "board_member", "resident" };
            if (request.TargetIds.Any(r => !validRoles.Contains(r)))
                throw AppException.UnprocessableEntity("Geçersiz rol.");
        }

        var currentUserId = _currentUser.UserId;
        var targetIdsJson = request.TargetIds is not null ? JsonSerializer.Serialize(request.TargetIds) : null;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.announcements
                SET title = @Title, body = @Body, category = @Category, priority = @Priority,
                    target_type = @TargetType, target_ids = @TargetIds::jsonb,
                    expires_at = @ExpiresAt, updated_by = @UpdatedBy
                WHERE id = @Id
                """,
                new
                {
                    Id = request.AnnouncementId,
                    Title = request.Title.Trim(),
                    Body = request.Body.Trim(),
                    request.Category,
                    request.Priority,
                    request.TargetType,
                    TargetIds = targetIdsJson,
                    ExpiresAt = request.ExpiresAt?.UtcDateTime,
                    UpdatedBy = currentUserId
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, old_values, new_values)
                VALUES ('announcements', @RecordId, @ActorId, 'update', @OldValues::jsonb, @NewValues::jsonb)
                """,
                new
                {
                    RecordId = request.AnnouncementId,
                    ActorId = currentUserId,
                    OldValues = JsonSerializer.Serialize(new { existing.Title, existing.Category, existing.Priority }),
                    NewValues = JsonSerializer.Serialize(new { Title = request.Title.Trim(), request.Category, request.Priority })
                }, tx);

            await tx.CommitAsync(ct);

            existing.Title = request.Title.Trim();
            existing.Body = request.Body.Trim();
            existing.Category = request.Category;
            existing.Priority = request.Priority;
            existing.TargetType = request.TargetType;
            existing.TargetIds = targetIdsJson;
            existing.ExpiresAt = request.ExpiresAt;
            existing.UpdatedBy = currentUserId;
            return existing;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
