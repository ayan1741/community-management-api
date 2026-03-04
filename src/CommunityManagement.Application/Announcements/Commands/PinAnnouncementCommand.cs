using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;
using System.Text.Json;

namespace CommunityManagement.Application.Announcements.Commands;

public record PinAnnouncementCommand(Guid OrgId, Guid AnnouncementId, bool IsPinned) : IRequest;

public class PinAnnouncementCommandHandler : IRequestHandler<PinAnnouncementCommand>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public PinAnnouncementCommandHandler(
        IAnnouncementRepository announcements, ICurrentUserService currentUser, IDbConnectionFactory factory)
    {
        _announcements = announcements;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task Handle(PinAnnouncementCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var existing = await _announcements.GetByIdAsync(request.AnnouncementId, ct)
            ?? throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.OrganizationId != request.OrgId)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.DeletedAt.HasValue)
            throw AppException.NotFound("Duyuru bulunamadı.");

        if (existing.Status != "published")
            throw AppException.UnprocessableEntity("Sadece yayınlanmış duyurular pinlenebilir.");

        // Pinleme limiti: en fazla 3
        if (request.IsPinned && !existing.IsPinned)
        {
            var pinnedCount = await _announcements.GetPinnedCountAsync(request.OrgId, ct);
            if (pinnedCount >= 3)
                throw AppException.UnprocessableEntity("En fazla 3 duyuru pinlenebilir.");
        }

        var currentUserId = _currentUser.UserId;

        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            await conn.ExecuteAsync(
                "UPDATE public.announcements SET is_pinned = @IsPinned, updated_by = @UpdatedBy WHERE id = @Id",
                new { Id = request.AnnouncementId, request.IsPinned, UpdatedBy = currentUserId }, tx);

            await conn.ExecuteAsync(
                """
                INSERT INTO public.audit_logs (table_name, record_id, actor_id, action, new_values)
                VALUES ('announcements', @RecordId, @ActorId, @Action, @NewValues::jsonb)
                """,
                new
                {
                    RecordId = request.AnnouncementId,
                    ActorId = currentUserId,
                    Action = request.IsPinned ? "pin" : "unpin",
                    NewValues = JsonSerializer.Serialize(new { request.IsPinned })
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
