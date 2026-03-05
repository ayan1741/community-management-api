using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record CreateAnnouncementCommand(
    Guid OrgId, string Title, string Body,
    string Category, string Priority,
    string TargetType, List<string>? TargetIds,
    DateTimeOffset? ExpiresAt
) : IRequest<Announcement>;

public class CreateAnnouncementCommandHandler : IRequestHandler<CreateAnnouncementCommand, Announcement>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public CreateAnnouncementCommandHandler(ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<Announcement> Handle(CreateAnnouncementCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        // Validasyon
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

        // Blok hedefleme: Blokların org'a ait olduğunu doğrula
        if (request.TargetType == "block" && request.TargetIds is not null)
        {
            using var checkConn = _factory.CreateUserConnection();
            var blockCount = await checkConn.QuerySingleAsync<long>(
                "SELECT COUNT(*) FROM public.blocks WHERE organization_id = @OrgId AND id = ANY(@BlockIds::uuid[]) AND deleted_at IS NULL",
                new { request.OrgId, BlockIds = request.TargetIds.ToArray() });

            if ((int)blockCount != request.TargetIds.Count)
                throw AppException.NotFound("Bir veya daha fazla blok bulunamadı.");
        }

        // Rol hedefleme: Geçerli rolleri kontrol et
        if (request.TargetType == "role" && request.TargetIds is not null)
        {
            var validRoles = new HashSet<string> { "admin", "board_member", "resident" };
            if (request.TargetIds.Any(r => !validRoles.Contains(r)))
                throw AppException.UnprocessableEntity("Geçersiz rol.");
        }

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;
        var targetIdsJson = request.TargetIds is not null ? JsonSerializer.Serialize(request.TargetIds) : null;

        var announcement = new Announcement
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            Title = request.Title.Trim(),
            Body = request.Body.Trim(),
            Category = request.Category,
            Priority = request.Priority,
            TargetType = request.TargetType,
            TargetIds = targetIdsJson,
            Status = "draft",
            IsPinned = false,
            ExpiresAt = request.ExpiresAt,
            CreatedBy = currentUserId,
            CreatedAt = now,
            UpdatedAt = now
        };

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.announcements
                    (id, organization_id, title, body, category, priority,
                     target_type, target_ids, status, is_pinned,
                     expires_at, created_by, created_at, updated_at)
                VALUES
                    (@Id, @OrganizationId, @Title, @Body, @Category, @Priority,
                     @TargetType, @TargetIds::jsonb, @Status, @IsPinned,
                     @ExpiresAt, @CreatedBy, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    announcement.Id,
                    announcement.OrganizationId,
                    announcement.Title,
                    announcement.Body,
                    announcement.Category,
                    announcement.Priority,
                    announcement.TargetType,
                    announcement.TargetIds,
                    announcement.Status,
                    announcement.IsPinned,
                    ExpiresAt = announcement.ExpiresAt?.UtcDateTime,
                    announcement.CreatedBy,
                    CreatedAt = announcement.CreatedAt.UtcDateTime,
                    UpdatedAt = announcement.UpdatedAt.UtcDateTime
                }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('announcements', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    RecordId = announcement.Id,
                    ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new
                    {
                        announcement.Title, announcement.Category, announcement.Priority,
                        announcement.TargetType, announcement.Status
                    })
                }, tx);

            await tx.CommitAsync(ct);
            return announcement;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
