using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.MaintenanceRequests.Commands;

public record DeleteMaintenanceRequestCommand(Guid OrgId, Guid Id) : IRequest;

public class DeleteMaintenanceRequestCommandHandler : IRequestHandler<DeleteMaintenanceRequestCommand>
{
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;
    private readonly IMaintenanceRequestRepository _repo;
    private readonly ISupabaseStorageService _storage;

    public DeleteMaintenanceRequestCommandHandler(
        ICurrentUserService currentUser, IDbConnectionFactory factory,
        IMaintenanceRequestRepository repo, ISupabaseStorageService storage)
    {
        _currentUser = currentUser;
        _factory = factory;
        _repo = repo;
        _storage = storage;
    }

    public async Task Handle(DeleteMaintenanceRequestCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var entity = await _repo.GetByIdAsync(request.Id, ct)
            ?? throw AppException.NotFound("Ariza bildirimi bulunamadi.");
        if (entity.OrganizationId != request.OrgId)
            throw AppException.NotFound("Ariza bildirimi bulunamadi.");

        var currentUserId = _currentUser.UserId;
        var now = DateTimeOffset.UtcNow;

        // DB soft delete
        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                """
                UPDATE public.maintenance_requests
                SET deleted_at = @Now, deleted_by = @UserId, updated_at = @Now
                WHERE id = @Id
                """,
                new { Now = now.UtcDateTime, UserId = currentUserId, request.Id }, tx);

            // Audit log
            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (organization_id, table_name, record_id, actor_id, action)
                VALUES (@OrgId, 'maintenance_requests', @RecordId, @ActorId, 'soft_delete')
                """,
                new { OrgId = request.OrgId, RecordId = request.Id, ActorId = currentUserId }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Storage silme — best-effort (transaction disinda)
        try
        {
            if (entity.PhotoUrls is not null)
            {
                var urls = JsonSerializer.Deserialize<List<string>>(entity.PhotoUrls);
                if (urls is not null)
                {
                    foreach (var url in urls)
                    {
                        await _storage.DeleteAsync("maintenance-attachments", ExtractPathFromUrl(url), ct);
                    }
                }
            }

            // Yorum fotograflarini da sil
            using var readConn = _factory.CreateServiceRoleConnection();
            var commentPhotos = await readConn.QueryAsync<string?>(
                "SELECT photo_url FROM public.maintenance_request_comments WHERE maintenance_request_id = @Id AND photo_url IS NOT NULL",
                new { request.Id });

            foreach (var photoUrl in commentPhotos)
            {
                if (photoUrl is not null)
                    await _storage.DeleteAsync("maintenance-attachments", ExtractPathFromUrl(photoUrl), ct);
            }
        }
        catch
        {
            // Best-effort — storage hataları yutulur
        }
    }

    private static string ExtractPathFromUrl(string url)
    {
        // URL formatı: .../storage/v1/object/public/maintenance-attachments/{path}
        // veya signed URL — path kısmını çıkarıyoruz
        var marker = "maintenance-attachments/";
        var idx = url.IndexOf(marker, StringComparison.Ordinal);
        return idx >= 0 ? url[(idx + marker.Length)..] : url;
    }
}
