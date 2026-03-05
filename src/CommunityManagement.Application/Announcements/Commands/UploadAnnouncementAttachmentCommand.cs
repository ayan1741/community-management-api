using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record UploadAnnouncementAttachmentCommand(
    Guid OrgId, Guid AnnouncementId,
    Stream FileStream, string FileName, string ContentType, long FileSize
) : IRequest<List<AttachmentInfo>>;

public class UploadAnnouncementAttachmentCommandHandler
    : IRequestHandler<UploadAnnouncementAttachmentCommand, List<AttachmentInfo>>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;
    private readonly ISupabaseStorageService _storage;
    private readonly IDbConnectionFactory _factory;

    public UploadAnnouncementAttachmentCommandHandler(
        IAnnouncementRepository announcements, ICurrentUserService currentUser,
        ISupabaseStorageService storage, IDbConnectionFactory factory)
    {
        _announcements = announcements;
        _currentUser = currentUser;
        _storage = storage;
        _factory = factory;
    }

    public async Task<List<AttachmentInfo>> Handle(UploadAnnouncementAttachmentCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.Status != "draft")
            throw AppException.Forbidden("Sadece taslak duyurulara dosya eklenebilir.");

        // Board member sadece kendi duyurusuna dosya ekleyebilir
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        if (role == MemberRole.BoardMember && existing.CreatedBy != _currentUser.UserId)
            throw AppException.Forbidden("Sadece kendi oluşturduğunuz duyuruya dosya ekleyebilirsiniz.");

        // Dosya doğrulama
        var allowedTypes = new HashSet<string> { "image/jpeg", "image/png", "image/webp", "application/pdf" };
        if (!allowedTypes.Contains(request.ContentType))
            throw AppException.UnprocessableEntity("Desteklenmeyen dosya formatı. Sadece JPEG, PNG, WebP ve PDF kabul edilir.");

        if (request.FileSize > 10 * 1024 * 1024) // 10MB
            throw AppException.UnprocessableEntity("Dosya boyutu en fazla 10MB olabilir.");

        // Mevcut ekleri kontrol et (max 5)
        var currentAttachments = string.IsNullOrEmpty(existing.AttachmentUrls)
            ? new List<AttachmentInfo>()
            : JsonSerializer.Deserialize<List<AttachmentInfo>>(existing.AttachmentUrls)!;

        if (currentAttachments.Count >= 5)
            throw AppException.UnprocessableEntity("Bir duyuruya en fazla 5 dosya eklenebilir.");

        // Storage'a yükle
        var path = $"{request.OrgId}/{request.AnnouncementId}/{request.FileName}";
        var url = await _storage.UploadAsync("announcement-attachments", path, request.FileStream, request.ContentType, ct);

        var newAttachment = new AttachmentInfo(url, request.FileName, request.FileSize);
        var newAttachmentJson = JsonSerializer.Serialize(new[] { newAttachment });

        // Atomic JSONB append — race condition'ı önler
        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        var updatedJson = await conn.QuerySingleOrDefaultAsync<string>(
            @"UPDATE public.announcements
              SET attachment_urls = COALESCE(attachment_urls, '[]'::jsonb) || @NewAtt::jsonb,
                  updated_by = @UpdatedBy,
                  updated_at = now()
              WHERE id = @Id
                AND jsonb_array_length(COALESCE(attachment_urls, '[]'::jsonb)) < 5
              RETURNING attachment_urls::text",
            new { Id = request.AnnouncementId, NewAtt = newAttachmentJson, UpdatedBy = _currentUser.UserId });

        if (updatedJson is null)
        {
            // 5 dosya limiti aşıldı — storage'dan yüklenen dosyayı temizle
            try { await _storage.DeleteAsync("announcement-attachments", path, ct); } catch { }
            throw AppException.UnprocessableEntity("Bir duyuruya en fazla 5 dosya eklenebilir.");
        }

        return JsonSerializer.Deserialize<List<AttachmentInfo>>(updatedJson)!;
    }
}
