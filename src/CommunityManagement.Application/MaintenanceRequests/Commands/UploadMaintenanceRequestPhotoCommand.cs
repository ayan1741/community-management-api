using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record UploadMaintenanceRequestPhotoCommand(
    Guid OrgId, Guid Id,
    Stream FileStream, string FileName, string ContentType, long FileLength
) : IRequest<string>;

public class UploadMaintenanceRequestPhotoCommandHandler : IRequestHandler<UploadMaintenanceRequestPhotoCommand, string>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;
    private readonly ISupabaseStorageService _storage;

    public UploadMaintenanceRequestPhotoCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory,
        IMaintenanceRequestRepository repo, ISupabaseStorageService storage)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
        _storage = storage;
    }

    public async Task<string> Handle(UploadMaintenanceRequestPhotoCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        if (request.ContentType is not ("image/jpeg" or "image/png" or "image/webp"))
            throw AppException.UnprocessableEntity("Desteklenmeyen dosya formati. Sadece JPEG, PNG ve WebP kabul edilir.");
        if (request.FileLength > 5 * 1024 * 1024)
            throw AppException.UnprocessableEntity("Dosya boyutu 5MB'yi asamaz.");

        // Mevcut fotograf sayisi kontrol (atomic SQL ile)
        var currentPhotos = entity.PhotoUrls is not null
            ? JsonSerializer.Deserialize<List<string>>(entity.PhotoUrls) ?? new()
            : new List<string>();

        if (currentPhotos.Count >= 5)
            throw AppException.UnprocessableEntity("En fazla 5 fotograf yuklenebilir.");

        // Supabase Storage'a yukle
        var fileId = Guid.NewGuid().ToString("N")[..8];
        var path = $"{request.OrgId}/{request.Id}/{fileId}_{request.FileName}";
        var url = await _storage.UploadAsync("maintenance-attachments", path, request.FileStream, request.ContentType, ct);

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);

        // Atomic append — race condition onlemi
        var affected = await conn.ExecuteAsync(
            """
            UPDATE public.maintenance_requests
            SET photo_urls = COALESCE(photo_urls, '[]'::jsonb) || @NewUrl::jsonb,
                updated_by = @UserId, updated_at = @Now
            WHERE id = @Id
              AND COALESCE(jsonb_array_length(photo_urls), 0) < 5
            """,
            new
            {
                NewUrl = JsonSerializer.Serialize(new[] { url }),
                UserId = currentUserId,
                Now = now.UtcDateTime,
                Id = request.Id
            });

        if (affected == 0)
            throw AppException.UnprocessableEntity("En fazla 5 fotograf yuklenebilir.");

        return url;
    }
}
