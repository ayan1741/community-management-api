using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class AgendaRepository : IAgendaRepository
{
    private readonly IDbConnectionFactory _factory;
    public AgendaRepository(IDbConnectionFactory factory) => _factory = factory;

    // -- Private Row Records --

    private record ItemRow(
        Guid Id, Guid OrganizationId, Guid? MeetingId, Guid CreatedBy,
        string Title, string? Description, string Category, string Status,
        bool IsPinned, string? CloseReason,
        int SupportCount, int CommentCount,
        DateTime? DeletedAt, Guid? DeletedBy,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record ListRow(
        Guid Id, string Title, string Category, string Status,
        bool IsPinned, int SupportCount, int CommentCount,
        string CreatedByName, Guid CreatedBy,
        bool HasUserSupport,
        DateTime CreatedAt, long TotalCount);

    private record DetailRow(
        Guid Id, Guid OrganizationId, Guid? MeetingId,
        string Title, string Description, string Category, string Status,
        bool IsPinned, string? CloseReason,
        int SupportCount, int CommentCount,
        string CreatedByName, Guid CreatedBy,
        bool HasUserSupport,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record SupportRow(Guid UserId, string UserName, DateTime CreatedAt);

    private record CommentRow(
        Guid Id, string Content, bool IsDeleted,
        string UserName, Guid UserId,
        DateTime CreatedAt, long TotalCount);

    private record StatsRow(
        long TotalOpen, long TotalUnderReview,
        long TotalVoting, long TotalDecided, long TotalClosed);

    // -- GetByIdAsync --

    public async Task<AgendaItem?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, meeting_id, created_by,
                   title, description, category, status,
                   is_pinned, close_reason,
                   support_count, comment_count,
                   deleted_at, deleted_by,
                   created_at, updated_at
            FROM public.agenda_items
            WHERE id = @Id AND deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<ItemRow>(sql, new { Id = id });
        if (row is null) return null;

        return new AgendaItem
        {
            Id = row.Id, OrganizationId = row.OrganizationId,
            MeetingId = row.MeetingId, CreatedBy = row.CreatedBy,
            Title = row.Title, Description = row.Description,
            Category = row.Category, Status = row.Status,
            IsPinned = row.IsPinned, CloseReason = row.CloseReason,
            SupportCount = row.SupportCount, CommentCount = row.CommentCount,
            DeletedAt = row.DeletedAt.HasValue ? new DateTimeOffset(row.DeletedAt.Value, TimeSpan.Zero) : null,
            DeletedBy = row.DeletedBy,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    // -- GetDetailAsync --

    public async Task<AgendaItemDetailDto?> GetDetailAsync(Guid id, Guid currentUserId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT ai.id, ai.organization_id, ai.meeting_id,
                   ai.title, COALESCE(ai.description, '') AS description,
                   ai.category, ai.status,
                   ai.is_pinned, ai.close_reason,
                   ai.support_count, ai.comment_count,
                   p.full_name AS created_by_name, ai.created_by,
                   EXISTS(SELECT 1 FROM public.agenda_supports s
                          WHERE s.agenda_item_id = ai.id AND s.user_id = @CurrentUserId) AS has_user_support,
                   ai.created_at, ai.updated_at
            FROM public.agenda_items ai
            JOIN public.profiles p ON p.id = ai.created_by
            WHERE ai.id = @Id AND ai.deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id, CurrentUserId = currentUserId });
        if (row is null) return null;

        return new AgendaItemDetailDto(
            row.Id, row.OrganizationId, row.MeetingId,
            row.Title, row.Description, row.Category, row.Status,
            row.IsPinned, row.CloseReason,
            row.SupportCount, row.CommentCount,
            row.CreatedByName, row.CreatedBy,
            row.HasUserSupport,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    // -- GetListAsync --

    public async Task<(IReadOnlyList<AgendaItemListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId,
        string? status, string? category, Guid? meetingId,
        string sortBy, string sortDirection,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder();
        sb.Append("""
            SELECT ai.id, ai.title, ai.category, ai.status,
                   ai.is_pinned, ai.support_count, ai.comment_count,
                   p.full_name AS created_by_name, ai.created_by,
                   EXISTS(SELECT 1 FROM public.agenda_supports s
                          WHERE s.agenda_item_id = ai.id AND s.user_id = @CurrentUserId) AS has_user_support,
                   ai.created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.agenda_items ai
            JOIN public.profiles p ON p.id = ai.created_by
            WHERE ai.organization_id = @OrgId AND ai.deleted_at IS NULL
            """);

        if (!string.IsNullOrEmpty(status)) sb.Append(" AND ai.status = @Status");
        if (!string.IsNullOrEmpty(category)) sb.Append(" AND ai.category = @Category");
        if (meetingId.HasValue) sb.Append(" AND ai.meeting_id = @MeetingId");

        // Siralama: sabitlenenler her zaman uste
        var orderCol = sortBy switch
        {
            "support" => "ai.support_count",
            _ => "ai.created_at"
        };
        var orderDir = sortDirection == "asc" ? "ASC" : "DESC";
        sb.Append($" ORDER BY ai.is_pinned DESC, {orderCol} {orderDir}");
        sb.Append(" LIMIT @PageSize OFFSET @Offset");

        var rows = await conn.QueryAsync<ListRow>(sb.ToString(), new
        {
            OrgId = orgId, CurrentUserId = currentUserId,
            Status = status, Category = category, MeetingId = meetingId,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        });

        var list = rows.Select(r => new AgendaItemListDto(
            r.Id, r.Title, r.Category, r.Status,
            r.IsPinned, r.SupportCount, r.CommentCount,
            r.CreatedByName, r.CreatedBy,
            r.HasUserSupport,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            r.TotalCount)).ToList();

        var totalCount = list.Count > 0 ? (int)list[0].TotalCount : 0;
        return (list, totalCount);
    }

    // -- GetStatsAsync --

    public async Task<AgendaStats> GetStatsAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                COUNT(*) FILTER (WHERE status = 'acik') AS total_open,
                COUNT(*) FILTER (WHERE status = 'degerlendiriliyor') AS total_under_review,
                COUNT(*) FILTER (WHERE status = 'oylamada') AS total_voting,
                COUNT(*) FILTER (WHERE status = 'kararlasti') AS total_decided,
                COUNT(*) FILTER (WHERE status = 'kapali') AS total_closed
            FROM public.agenda_items
            WHERE organization_id = @OrgId AND deleted_at IS NULL
            """;
        var row = await conn.QuerySingleAsync<StatsRow>(sql, new { OrgId = orgId });
        return new AgendaStats(
            (int)row.TotalOpen, (int)row.TotalUnderReview,
            (int)row.TotalVoting, (int)row.TotalDecided, (int)row.TotalClosed);
    }

    // -- HasUserSupportAsync --

    public async Task<bool> HasUserSupportAsync(Guid agendaItemId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            "SELECT COUNT(*) FROM public.agenda_supports WHERE agenda_item_id = @AiId AND user_id = @UserId",
            new { AiId = agendaItemId, UserId = userId });
        return count > 0;
    }

    // -- GetSupportersAsync --

    public async Task<IReadOnlyList<AgendaSupportDetailDto>> GetSupportersAsync(Guid agendaItemId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT s.user_id, p.full_name AS user_name, s.created_at
            FROM public.agenda_supports s
            JOIN public.profiles p ON p.id = s.user_id
            WHERE s.agenda_item_id = @AiId
            ORDER BY s.created_at ASC
            """;
        var rows = await conn.QueryAsync<SupportRow>(sql, new { AiId = agendaItemId });
        return rows.Select(r => new AgendaSupportDetailDto(
            r.UserId, r.UserName,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();
    }

    // -- GetCommentsAsync --

    public async Task<(IReadOnlyList<AgendaCommentDto> Items, int TotalCount)> GetCommentsAsync(
        Guid agendaItemId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT c.id, c.content, c.is_deleted,
                   p.full_name AS user_name, c.user_id,
                   c.created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.agenda_comments c
            JOIN public.profiles p ON p.id = c.user_id
            WHERE c.agenda_item_id = @AiId
            ORDER BY c.created_at ASC
            LIMIT @PageSize OFFSET @Offset
            """;
        var rows = await conn.QueryAsync<CommentRow>(sql, new
        {
            AiId = agendaItemId,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        });

        var rowList = rows.ToList();
        var list = rowList.Select(r => new AgendaCommentDto(
            r.Id,
            r.IsDeleted ? "[Bu yorum silindi]" : r.Content,
            r.IsDeleted,
            r.UserName, r.UserId,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();

        var totalCount = rowList.Count > 0 ? (int)rowList[0].TotalCount : 0;
        return (list, totalCount);
    }

    // -- GetCommentByIdAsync --

    public async Task<AgendaComment?> GetCommentByIdAsync(Guid commentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, agenda_item_id, user_id, content, is_deleted, created_at
            FROM public.agenda_comments
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<AgendaComment>(sql, new { Id = commentId });
        return row;
    }

    // -- Rate limit counts --

    public async Task<int> CountUserAgendaItemsTodayAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            """
            SELECT COUNT(*) FROM public.agenda_items
            WHERE organization_id = @OrgId AND created_by = @UserId
              AND created_at >= CURRENT_DATE AND deleted_at IS NULL
            """,
            new { OrgId = orgId, UserId = userId });
        return (int)count;
    }

    public async Task<int> CountUserAgendaItemsLastHourAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            """
            SELECT COUNT(*) FROM public.agenda_items
            WHERE organization_id = @OrgId AND created_by = @UserId
              AND created_at >= now() - interval '1 hour' AND deleted_at IS NULL
            """,
            new { OrgId = orgId, UserId = userId });
        return (int)count;
    }

    public async Task<int> CountUserCommentsLastHourAsync(Guid agendaItemId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            """
            SELECT COUNT(*) FROM public.agenda_comments
            WHERE agenda_item_id = @AiId AND user_id = @UserId
              AND created_at >= now() - interval '1 hour'
            """,
            new { AiId = agendaItemId, UserId = userId });
        return (int)count;
    }
}
