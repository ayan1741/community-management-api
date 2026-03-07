using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record AddMaintenanceRequestCommentCommand(
    Guid OrgId, Guid Id, string Content,
    Stream? PhotoStream, string? PhotoFileName,
    string? PhotoContentType, long PhotoLength
) : IRequest<MaintenanceRequestComment>;

public class AddMaintenanceRequestCommentCommandHandler : IRequestHandler<AddMaintenanceRequestCommentCommand, MaintenanceRequestComment>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;
    private readonly ISupabaseStorageService _storage;

    public AddMaintenanceRequestCommentCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory,
        IMaintenanceRequestRepository repo, ISupabaseStorageService storage)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
        _storage = storage;
    }

    public async Task<MaintenanceRequestComment> Handle(AddMaintenanceRequestCommentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        if (entity.Status is "closed" or "cancelled")
            throw AppException.UnprocessableEntity("Kapatilmis veya iptal edilmis arizaya yorum eklenemez.");

        var currentUserId = _currentUser.UserId;
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);

        // Sakin sadece kendi arizasina yorum ekleyebilir
        if (role == MemberRole.Resident && entity.ReportedBy != currentUserId)
            throw AppException.Forbidden("Bu arizaya yorum ekleme yetkiniz yok.");

        if (string.IsNullOrWhiteSpace(request.Content))
            throw AppException.UnprocessableEntity("Yorum icerigi zorunludur.");

        var commentId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        string? photoUrl = null;

        // Fotograf yukle
        if (request.PhotoStream is not null)
        {
            if (request.PhotoContentType is not ("image/jpeg" or "image/png" or "image/webp"))
                throw AppException.UnprocessableEntity("Desteklenmeyen dosya formatı. Sadece JPEG, PNG ve WebP kabul edilir.");
            if (request.PhotoLength > 5 * 1024 * 1024)
                throw AppException.UnprocessableEntity("Dosya boyutu 5MB'yi asamaz.");

            var path = $"{request.OrgId}/{request.Id}/comments/{commentId}/{request.PhotoFileName}";
            photoUrl = await _storage.UploadAsync("maintenance-attachments", path, request.PhotoStream, request.PhotoContentType, ct);
        }

        var comment = new MaintenanceRequestComment
        {
            Id = commentId,
            MaintenanceRequestId = request.Id,
            Content = request.Content.Trim(),
            PhotoUrl = photoUrl,
            CreatedBy = currentUserId,
            CreatedAt = now
        };

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO public.maintenance_request_comments
                    (id, maintenance_request_id, content, photo_url, created_by, created_at)
                VALUES (@Id, @MaintenanceRequestId, @Content, @PhotoUrl, @CreatedBy, @CreatedAt)
                """,
                new
                {
                    comment.Id, comment.MaintenanceRequestId, comment.Content,
                    comment.PhotoUrl, comment.CreatedBy,
                    CreatedAt = comment.CreatedAt.UtcDateTime
                }, tx);

            // Karsi tarafa bildirim: sakin→admin, admin→sakin
            if (role == MemberRole.Resident)
            {
                // Sakin yazdi → admin'lere bildirim
                var admins = await conn.QueryAsync<Guid>(
                    """
                    SELECT user_id FROM public.organization_members
                    WHERE organization_id = @OrgId AND role = 'admin' AND status = 'active'
                    """,
                    new { request.OrgId }, tx);

                foreach (var adminId in admins)
                {
                    await conn.ExecuteAsync(
                        """
                        INSERT INTO public.notifications
                            (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                        VALUES (@Id, @OrgId, @UserId, 'maintenance_comment', @Title, @Body,
                                'maintenance_request', @RefId, @Now)
                        """,
                        new
                        {
                            Id = Guid.NewGuid(), request.OrgId, UserId = adminId,
                            Title = $"Yeni Yorum: {entity.Title}",
                            Body = comment.Content.Length > 100 ? comment.Content[..100] + "..." : comment.Content,
                            RefId = request.Id, Now = now.UtcDateTime
                        }, tx);
                }
            }
            else
            {
                // Admin/YK yazdi → bildiren sakine bildirim
                await conn.ExecuteAsync(
                    """
                    INSERT INTO public.notifications
                        (id, organization_id, user_id, type, title, body, reference_type, reference_id, created_at)
                    VALUES (@Id, @OrgId, @UserId, 'maintenance_comment', @Title, @Body,
                            'maintenance_request', @RefId, @Now)
                    """,
                    new
                    {
                        Id = Guid.NewGuid(), request.OrgId, UserId = entity.ReportedBy,
                        Title = $"Yeni Yorum: {entity.Title}",
                        Body = comment.Content.Length > 100 ? comment.Content[..100] + "..." : comment.Content,
                        RefId = request.Id, Now = now.UtcDateTime
                    }, tx);
            }

            // Email job
            await conn.ExecuteAsync(
                """
                INSERT INTO public.background_jobs (job_type, payload, status, created_at)
                VALUES ('maintenance_status_email', @Payload::jsonb, 'queued', @Now)
                """,
                new
                {
                    Payload = JsonSerializer.Serialize(new
                    {
                        MaintenanceRequestId = request.Id,
                        OrganizationId = request.OrgId,
                        NewStatus = "comment",
                        CommentBy = currentUserId,
                        CommentContent = comment.Content
                    }),
                    Now = now.UtcDateTime
                }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action, new_values)
                VALUES (@OrgId, 'maintenance_request_comments', @RecordId, @ActorId, 'insert', @NewValues::jsonb)
                """,
                new
                {
                    OrgId = request.OrgId, RecordId = comment.Id, ActorId = currentUserId,
                    NewValues = JsonSerializer.Serialize(new { comment.Content, MaintenanceRequestId = request.Id })
                }, tx);

            await tx.CommitAsync(ct);
            return comment;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
