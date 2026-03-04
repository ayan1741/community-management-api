using CommunityManagement.Core.Common;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly IDbConnectionFactory _factory;

    public NotificationRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<(IReadOnlyList<NotificationListItem> Items, int TotalCount)> GetByUserAsync(
        Guid userId, Guid orgId, bool? isRead, string? type,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder("""
            SELECT id, type, title, body, reference_type, reference_id,
                   is_read, read_at, created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.notifications
            WHERE user_id = @UserId AND organization_id = @OrgId
            """);

        if (isRead.HasValue)
            sb.Append(isRead.Value ? " AND is_read = true" : " AND is_read = false");
        if (type is not null)
            sb.Append(" AND type = @Type");

        sb.Append(" ORDER BY is_read ASC, created_at DESC");
        sb.Append(" LIMIT @PageSize OFFSET @Offset");

        var rows = (await conn.QueryAsync<NotifRow>(sb.ToString(), new
        {
            UserId = userId,
            OrgId = orgId,
            Type = type,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new NotificationListItem(
            r.Id, r.Type, r.Title, r.Body,
            r.ReferenceType, r.ReferenceId,
            r.IsRead,
            r.ReadAt.HasValue ? new DateTimeOffset(r.ReadAt.Value, TimeSpan.Zero) : null,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            (int)r.TotalCount
        )).ToList();

        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM public.notifications WHERE user_id = @UserId AND organization_id = @OrgId AND is_read = false",
            new { UserId = userId, OrgId = orgId });
        return (int)count;
    }

    public async Task MarkReadAsync(Guid userId, Guid orgId, IReadOnlyList<Guid>? notificationIds, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        if (notificationIds is null || notificationIds.Count == 0)
        {
            // Tümünü okundu işaretle (org-scoped)
            await conn.ExecuteAsync(
                "UPDATE public.notifications SET is_read = true, read_at = now() WHERE user_id = @UserId AND organization_id = @OrgId AND is_read = false",
                new { UserId = userId, OrgId = orgId });
        }
        else
        {
            // Belirli ID'leri okundu işaretle (org-scoped)
            await conn.ExecuteAsync(
                "UPDATE public.notifications SET is_read = true, read_at = now() WHERE user_id = @UserId AND organization_id = @OrgId AND id = ANY(@Ids) AND is_read = false",
                new { UserId = userId, OrgId = orgId, Ids = notificationIds.ToArray() });
        }
    }

    public async Task DeleteByReferenceAsync(Guid referenceId, string referenceType, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        await conn.ExecuteAsync(
            "DELETE FROM public.notifications WHERE reference_id = @ReferenceId AND reference_type = @ReferenceType",
            new { ReferenceId = referenceId, ReferenceType = referenceType });
    }

    // ── Private Row Records ─────────────────────────────────────────

    private record NotifRow(
        Guid Id, string Type, string Title, string? Body,
        string? ReferenceType, Guid? ReferenceId,
        bool IsRead, DateTime? ReadAt, DateTime CreatedAt,
        long TotalCount);
}
