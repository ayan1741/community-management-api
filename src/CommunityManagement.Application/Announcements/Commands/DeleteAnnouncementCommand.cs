using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record DeleteAnnouncementCommand(Guid OrgId, Guid AnnouncementId) : IRequest;

public record AttachmentInfo(string Url, string Name, long Size);

public class DeleteAnnouncementCommandHandler : IRequestHandler<DeleteAnnouncementCommand>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly ISupabaseStorageService _storage;

    public DeleteAnnouncementCommandHandler(
        IAnnouncementRepository announcements, INotificationRepository notifications,
        ICurrentUserService currentUser, IDbConnectionFactory factory,
        ISupabaseStorageService storage)
    {
        _announcements = announcements;
        _notifications = notifications;
        _currentUser = currentUser;
        _factory = factory;
        _storage = storage;
    }

    public async Task Handle(DeleteAnnouncementCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.DeletedAt.HasValue)
            throw AppException.NotFound("Duyuru bulunamadı.");

        var currentUserId = _currentUser.UserId;

        // Storage'daki ek dosyaları sil (orphaned dosya önlemi)
        if (!string.IsNullOrEmpty(existing.AttachmentUrls))
        {
            var attachments = JsonSerializer.Deserialize<List<AttachmentInfo>>(existing.AttachmentUrls);
            if (attachments?.Count > 0)
            {
                foreach (var att in attachments)
                {
                    var path = $"{existing.OrganizationId}/{existing.Id}/{att.Name}";
                    try { await _storage.DeleteAsync("announcement-attachments", path, ct); }
                    catch { /* Storage silme hatası DB işlemini engellemez */ }
                }
            }
        }

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // Soft delete
            await conn.ExecuteAsync(
                "UPDATE public.announcements SET deleted_at = now(), deleted_by = @DeletedBy WHERE id = @Id",
                new { Id = request.AnnouncementId, DeletedBy = currentUserId }, tx);

            // İlgili bildirimleri sil (hard delete — notifications tablosunda deleted_at yok)
            await conn.ExecuteAsync(
                "DELETE FROM public.notifications WHERE reference_id = @ReferenceId AND reference_type = 'announcement'",
                new { ReferenceId = request.AnnouncementId }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, old_values)
                VALUES ('announcements', @RecordId, @ActorId, 'delete', @OldValues::jsonb)
                """,
                new
                {
                    RecordId = request.AnnouncementId,
                    ActorId = currentUserId,
                    OldValues = JsonSerializer.Serialize(new { existing.Title, existing.Status })
                }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
