using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class AnnouncementRepository : IAnnouncementRepository
{
    private readonly IDbConnectionFactory _factory;

    public AnnouncementRepository(IDbConnectionFactory factory) => _factory = factory;

    public async Task<Announcement?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, title, body, category, priority,
                   target_type, target_ids::text, status, is_pinned,
                   published_at, expires_at, attachment_urls::text,
                   target_member_count,
                   created_by, updated_by, deleted_at, deleted_by,
                   created_at, updated_at
            FROM public.announcements
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<AnnouncementRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<AnnouncementDetail?> GetDetailAsync(Guid id, Guid currentUserId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT a.id, a.organization_id, a.title, a.body,
                   a.category, a.priority, a.target_type, a.target_ids::text AS target_ids,
                   a.status, a.is_pinned,
                   a.published_at, a.expires_at,
                   a.attachment_urls::text AS attachment_urls,
                   a.target_member_count,
                   p.full_name AS created_by_name,
                   a.created_by, a.updated_by,
                   a.created_at, a.updated_at,
                   CASE WHEN ar.id IS NOT NULL THEN true ELSE false END AS is_read
            FROM public.announcements a
            JOIN public.profiles p ON p.id = a.created_by
            LEFT JOIN public.announcement_reads ar
              ON ar.announcement_id = a.id AND ar.user_id = @CurrentUserId
            WHERE a.id = @Id AND a.deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id, CurrentUserId = currentUserId });
        if (row is null) return null;

        return new AnnouncementDetail(
            row.Id, row.OrganizationId, row.Title, row.Body,
            row.Category, row.Priority, row.TargetType, row.TargetIds,
            row.Status, row.IsPinned,
            row.PublishedAt.HasValue ? new DateTimeOffset(row.PublishedAt.Value, TimeSpan.Zero) : null,
            row.ExpiresAt.HasValue ? new DateTimeOffset(row.ExpiresAt.Value, TimeSpan.Zero) : null,
            row.AttachmentUrls, row.TargetMemberCount,
            row.CreatedByName, row.CreatedBy, row.UpdatedBy,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero),
            row.IsRead);
    }

    public async Task<(IReadOnlyList<AnnouncementListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, Guid currentUserId,
        string? category, string? priority, string? status,
        bool includeExpired, bool includeDrafts, bool filterByTarget,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder("""
            SELECT a.id, a.title, a.category, a.priority, a.status, a.is_pinned,
                   p.full_name AS created_by_name,
                   a.published_at, a.created_at,
                   CASE WHEN ar.id IS NOT NULL THEN true ELSE false END AS is_read,
                   COUNT(*) OVER() AS total_count
            FROM public.announcements a
            JOIN public.profiles p ON p.id = a.created_by
            LEFT JOIN public.announcement_reads ar
              ON ar.announcement_id = a.id AND ar.user_id = @CurrentUserId
            WHERE a.organization_id = @OrgId AND a.deleted_at IS NULL
            """);

        if (!includeDrafts)
            sb.Append(" AND a.status != 'draft'");
        if (!includeExpired)
            sb.Append("""
                 AND a.status != 'expired'
                 AND (a.expires_at IS NULL OR a.expires_at > now())
                """);
        if (category is not null)
            sb.Append(" AND a.category = @Category");
        if (priority is not null)
            sb.Append(" AND a.priority = @Priority");
        if (status is not null)
            sb.Append(" AND a.status = @Status");

        // Sakinler sadece kendilerini hedefleyen duyuruları görür
        if (filterByTarget)
            sb.Append("""
                 AND (
                    a.target_type = 'all'
                    OR (a.target_type = 'block' AND EXISTS (
                        SELECT 1 FROM public.unit_residents ur
                        JOIN public.units u ON u.id = ur.unit_id
                        WHERE ur.user_id = @CurrentUserId AND ur.status = 'active'
                        AND u.block_id IN (SELECT jsonb_array_elements_text(a.target_ids)::uuid)
                    ))
                    OR (a.target_type = 'role' AND EXISTS (
                        SELECT 1 FROM public.organization_members om2
                        WHERE om2.user_id = @CurrentUserId AND om2.organization_id = a.organization_id
                        AND om2.status = 'active'
                        AND om2.role IN (SELECT jsonb_array_elements_text(a.target_ids))
                    ))
                )
                """);

        sb.Append(" ORDER BY a.is_pinned DESC, a.published_at DESC NULLS LAST, a.created_at DESC");
        sb.Append(" LIMIT @PageSize OFFSET @Offset");

        var rows = (await conn.QueryAsync<ListRow>(sb.ToString(), new
        {
            OrgId = orgId,
            CurrentUserId = currentUserId,
            Category = category,
            Priority = priority,
            Status = status,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new AnnouncementListItem(
            r.Id, r.Title, r.Category, r.Priority,
            r.Status, r.IsPinned,
            r.CreatedByName,
            r.PublishedAt.HasValue ? new DateTimeOffset(r.PublishedAt.Value, TimeSpan.Zero) : null,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            r.IsRead,
            (int)r.TotalCount
        )).ToList();

        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<int> GetPinnedCountAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM public.announcements WHERE organization_id = @OrgId AND is_pinned = true AND deleted_at IS NULL",
            new { OrgId = orgId });
        return (int)count;
    }

    public async Task<(IReadOnlyList<AnnouncementReadItem> Readers, int TotalCount)> GetReadersAsync(
        Guid announcementId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT ar.user_id, p.full_name, ar.read_at,
                   COUNT(*) OVER() AS total_count
            FROM public.announcement_reads ar
            JOIN public.profiles p ON p.id = ar.user_id
            WHERE ar.announcement_id = @AnnouncementId
            ORDER BY ar.read_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;
        var rows = (await conn.QueryAsync<ReaderRow>(sql, new
        {
            AnnouncementId = announcementId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new AnnouncementReadItem(
            r.UserId, r.FullName,
            new DateTimeOffset(r.ReadAt, TimeSpan.Zero)
        )).ToList();

        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<(IReadOnlyList<AnnouncementUnreadItem> NonReaders, int TotalCount)> GetNonReadersAsync(
        Guid announcementId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT om.user_id, p.full_name,
                   COUNT(*) OVER() AS total_count
            FROM public.organization_members om
            JOIN public.profiles p ON p.id = om.user_id
            JOIN public.announcements a ON a.organization_id = om.organization_id
            WHERE a.id = @AnnouncementId
              AND om.status = 'active'
              AND (
                  a.target_type = 'all'
                  OR (a.target_type = 'block' AND EXISTS (
                      SELECT 1 FROM public.unit_residents ur
                      JOIN public.units u ON u.id = ur.unit_id
                      WHERE ur.user_id = om.user_id AND ur.status = 'active'
                      AND u.block_id IN (SELECT jsonb_array_elements_text(a.target_ids)::uuid)
                  ))
                  OR (a.target_type = 'role' AND om.role IN (
                      SELECT jsonb_array_elements_text(a.target_ids)))
              )
              AND NOT EXISTS (
                  SELECT 1 FROM public.announcement_reads ar
                  WHERE ar.announcement_id = @AnnouncementId AND ar.user_id = om.user_id
              )
            ORDER BY p.full_name
            LIMIT @PageSize OFFSET @Offset
            """;
        var rows = (await conn.QueryAsync<UnreadRow>(sql, new
        {
            AnnouncementId = announcementId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new AnnouncementUnreadItem(r.UserId, r.FullName)).ToList();
        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<int> GetReadCountAsync(Guid announcementId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM public.announcement_reads WHERE announcement_id = @AnnouncementId",
            new { AnnouncementId = announcementId });
        return (int)count;
    }

    // ── Private Row Records ──────────────────────────────────────────────

    private static Announcement MapRow(AnnouncementRow r) => new()
    {
        Id = r.Id, OrganizationId = r.OrganizationId,
        Title = r.Title, Body = r.Body,
        Category = r.Category, Priority = r.Priority,
        TargetType = r.TargetType, TargetIds = r.TargetIds,
        Status = r.Status, IsPinned = r.IsPinned,
        PublishedAt = r.PublishedAt.HasValue ? new DateTimeOffset(r.PublishedAt.Value, TimeSpan.Zero) : null,
        ExpiresAt = r.ExpiresAt.HasValue ? new DateTimeOffset(r.ExpiresAt.Value, TimeSpan.Zero) : null,
        AttachmentUrls = r.AttachmentUrls,
        TargetMemberCount = r.TargetMemberCount,
        CreatedBy = r.CreatedBy, UpdatedBy = r.UpdatedBy,
        DeletedAt = r.DeletedAt.HasValue ? new DateTimeOffset(r.DeletedAt.Value, TimeSpan.Zero) : null,
        DeletedBy = r.DeletedBy,
        CreatedAt = new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero)
    };

    // DB'den dönen tip: timestamptz → DateTime (Npgsql 9.x), jsonb → text cast
    private record AnnouncementRow(
        Guid Id, Guid OrganizationId, string Title, string Body,
        string Category, string Priority, string TargetType, string? TargetIds,
        string Status, bool IsPinned,
        DateTime? PublishedAt, DateTime? ExpiresAt,
        string? AttachmentUrls, int? TargetMemberCount,
        Guid CreatedBy, Guid? UpdatedBy,
        DateTime? DeletedAt, Guid? DeletedBy,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record DetailRow(
        Guid Id, Guid OrganizationId, string Title, string Body,
        string Category, string Priority, string TargetType, string? TargetIds,
        string Status, bool IsPinned,
        DateTime? PublishedAt, DateTime? ExpiresAt,
        string? AttachmentUrls, int? TargetMemberCount,
        string CreatedByName, Guid CreatedBy, Guid? UpdatedBy,
        DateTime CreatedAt, DateTime UpdatedAt,
        bool IsRead);

    private record ListRow(
        Guid Id, string Title, string Category, string Priority,
        string Status, bool IsPinned,
        string CreatedByName,
        DateTime? PublishedAt, DateTime CreatedAt,
        bool IsRead, long TotalCount);

    private record ReaderRow(Guid UserId, string FullName, DateTime ReadAt, long TotalCount);
    private record UnreadRow(Guid UserId, string FullName, long TotalCount);
}
