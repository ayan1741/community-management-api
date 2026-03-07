using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class MaintenanceRequestRepository : IMaintenanceRequestRepository
{
    private readonly IDbConnectionFactory _factory;
    public MaintenanceRequestRepository(IDbConnectionFactory factory) => _factory = factory;

    // -- Private Row Records (Dapper positional mapping) --

    private record RequestRow(
        Guid Id, Guid OrganizationId, string Title, string Description,
        string Category, string Priority, string Status,
        string LocationType, Guid? UnitId, string? LocationNote,
        string? AssigneeName, string? AssigneePhone, string? AssigneeNote,
        DateTime? AssignedAt,
        decimal TotalCost, bool IsRecurring,
        short? SatisfactionRating, string? SatisfactionComment, DateTime? RatedAt,
        DateTime? SlaDeadlineAt, bool SlaBreached,
        string? PhotoUrls,
        Guid ReportedBy,
        DateTime? ResolvedAt, DateTime? ClosedAt,
        DateTime? CancelledAt, Guid? CancelledBy,
        Guid CreatedBy, Guid? UpdatedBy,
        DateTime? DeletedAt, Guid? DeletedBy,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record ListRow(
        Guid Id, string Title, string Category, string Priority,
        string Status, string LocationType, string? LocationNote,
        string ReportedByName, int PhotoCount,
        bool IsRecurring, bool SlaBreached,
        DateTime CreatedAt, long TotalCount);

    private record DetailRow(
        Guid Id, Guid OrganizationId,
        string Title, string Description, string Category, string Priority,
        string Status, string LocationType, Guid? UnitId, string? UnitLabel,
        string? LocationNote,
        string? AssigneeName, string? AssigneePhone, string? AssigneeNote,
        DateTime? AssignedAt,
        decimal TotalCost, bool IsRecurring,
        short? SatisfactionRating, string? SatisfactionComment, DateTime? RatedAt,
        DateTime? SlaDeadlineAt, bool SlaBreached,
        string? PhotoUrls,
        string ReportedByName, Guid ReportedBy,
        DateTime? ResolvedAt, DateTime? ClosedAt,
        DateTime? CancelledAt,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record LogRow(
        Guid Id, string? FromStatus, string ToStatus,
        string? Note, string CreatedByName, DateTime CreatedAt);

    private record CommentRow(
        Guid Id, string Content, string? PhotoUrl,
        string CreatedByName, Guid CreatedBy, DateTime CreatedAt);

    private record CostRow(
        Guid Id, decimal Amount, string? Description,
        Guid? FinanceRecordId, string CreatedByName, DateTime CreatedAt);

    private record StatsRow(
        long TotalOpen, long TotalResolved, long TotalClosed,
        long SlaBreachedCount, long RecurringCount,
        decimal TotalCostSum);

    // -- GetByIdAsync --

    public async Task<MaintenanceRequest?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, title, description,
                   category, priority, status,
                   location_type, unit_id, location_note,
                   assignee_name, assignee_phone, assignee_note, assigned_at,
                   total_cost, is_recurring,
                   satisfaction_rating, satisfaction_comment, rated_at,
                   sla_deadline_at, sla_breached,
                   photo_urls::text,
                   reported_by,
                   resolved_at, closed_at,
                   cancelled_at, cancelled_by,
                   created_by, updated_by,
                   deleted_at, deleted_by,
                   created_at, updated_at
            FROM public.maintenance_requests
            WHERE id = @Id AND deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<RequestRow>(sql, new { Id = id });
        return row is null ? null : MapToEntity(row);
    }

    // -- GetDetailAsync --

    public async Task<MaintenanceRequestDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT mr.id, mr.organization_id,
                   mr.title, mr.description, mr.category, mr.priority,
                   mr.status, mr.location_type, mr.unit_id,
                   CASE WHEN u.id IS NOT NULL
                        THEN b.name || ' — ' || u.unit_number
                        ELSE NULL END AS unit_label,
                   mr.location_note,
                   mr.assignee_name, mr.assignee_phone, mr.assignee_note, mr.assigned_at,
                   mr.total_cost, mr.is_recurring,
                   mr.satisfaction_rating, mr.satisfaction_comment, mr.rated_at,
                   mr.sla_deadline_at, mr.sla_breached,
                   mr.photo_urls::text,
                   p.full_name AS reported_by_name, mr.reported_by,
                   mr.resolved_at, mr.closed_at, mr.cancelled_at,
                   mr.created_at, mr.updated_at
            FROM public.maintenance_requests mr
            JOIN public.profiles p ON p.id = mr.reported_by
            LEFT JOIN public.units u ON u.id = mr.unit_id
            LEFT JOIN public.blocks b ON b.id = u.block_id
            WHERE mr.id = @Id AND mr.deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id });
        if (row is null) return null;

        return new MaintenanceRequestDetailDto(
            row.Id, row.OrganizationId,
            row.Title, row.Description, row.Category, row.Priority,
            row.Status, row.LocationType, row.UnitId, row.UnitLabel,
            row.LocationNote,
            row.AssigneeName, row.AssigneePhone, row.AssigneeNote,
            row.AssignedAt.HasValue ? new DateTimeOffset(row.AssignedAt.Value, TimeSpan.Zero) : null,
            row.TotalCost, row.IsRecurring,
            row.SatisfactionRating, row.SatisfactionComment,
            row.RatedAt.HasValue ? new DateTimeOffset(row.RatedAt.Value, TimeSpan.Zero) : null,
            row.SlaDeadlineAt.HasValue ? new DateTimeOffset(row.SlaDeadlineAt.Value, TimeSpan.Zero) : null,
            row.SlaBreached,
            row.PhotoUrls,
            row.ReportedByName, row.ReportedBy,
            row.ResolvedAt.HasValue ? new DateTimeOffset(row.ResolvedAt.Value, TimeSpan.Zero) : null,
            row.ClosedAt.HasValue ? new DateTimeOffset(row.ClosedAt.Value, TimeSpan.Zero) : null,
            row.CancelledAt.HasValue ? new DateTimeOffset(row.CancelledAt.Value, TimeSpan.Zero) : null,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    // -- GetListAsync --

    public async Task<(IReadOnlyList<MaintenanceRequestListItem> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId, string currentUserRole,
        Guid[]? currentUserUnitIds,
        string? status, string? category, string? priority, string? locationType,
        bool? isRecurring, bool? slaBreached,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder();
        sb.Append("""
            SELECT mr.id, mr.title, mr.category, mr.priority,
                   mr.status, mr.location_type, mr.location_note,
                   p.full_name AS reported_by_name,
                   COALESCE(jsonb_array_length(mr.photo_urls), 0)::int AS photo_count,
                   mr.is_recurring, mr.sla_breached,
                   mr.created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.maintenance_requests mr
            JOIN public.profiles p ON p.id = mr.reported_by
            WHERE mr.organization_id = @OrgId AND mr.deleted_at IS NULL
            """);

        // Sakin gorunurluk filtresi (uygulama katmaninda)
        if (currentUserRole == "resident")
        {
            sb.Append("""
                 AND (
                    mr.reported_by = @CurrentUserId
                    OR mr.location_type = 'common_area'
                    OR mr.unit_id = ANY(@CurrentUserUnitIds)
                 )
                """);
        }

        if (!string.IsNullOrEmpty(status)) sb.Append(" AND mr.status = @Status");
        if (!string.IsNullOrEmpty(category)) sb.Append(" AND mr.category = @Category");
        if (!string.IsNullOrEmpty(priority)) sb.Append(" AND mr.priority = @Priority");
        if (!string.IsNullOrEmpty(locationType)) sb.Append(" AND mr.location_type = @LocationType");
        if (isRecurring.HasValue) sb.Append(" AND mr.is_recurring = @IsRecurring");
        if (slaBreached.HasValue) sb.Append(" AND mr.sla_breached = @SlaBreached");

        sb.Append(" ORDER BY mr.created_at DESC LIMIT @PageSize OFFSET @Offset");

        var rows = await conn.QueryAsync<ListRow>(sb.ToString(), new
        {
            OrgId = orgId,
            CurrentUserId = currentUserId,
            CurrentUserUnitIds = currentUserUnitIds ?? Array.Empty<Guid>(),
            Status = status,
            Category = category,
            Priority = priority,
            LocationType = locationType,
            IsRecurring = isRecurring,
            SlaBreached = slaBreached,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        });

        var list = rows.Select(r => new MaintenanceRequestListItem(
            r.Id, r.Title, r.Category, r.Priority,
            r.Status, r.LocationType, r.LocationNote,
            r.ReportedByName, r.PhotoCount,
            r.IsRecurring, r.SlaBreached,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            r.TotalCount)).ToList();

        var totalCount = list.Count > 0 ? (int)list[0].TotalCount : 0;
        return (list, totalCount);
    }

    // -- GetStatsAsync --

    public async Task<MaintenanceRequestStats> GetStatsAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                COUNT(*) FILTER (WHERE status IN ('reported','in_review','assigned','in_progress')) AS total_open,
                COUNT(*) FILTER (WHERE status = 'resolved') AS total_resolved,
                COUNT(*) FILTER (WHERE status = 'closed') AS total_closed,
                COUNT(*) FILTER (WHERE sla_breached = true AND status NOT IN ('closed','cancelled')) AS sla_breached_count,
                COUNT(*) FILTER (WHERE is_recurring = true AND status NOT IN ('closed','cancelled')) AS recurring_count,
                COALESCE(SUM(total_cost), 0) AS total_cost_sum
            FROM public.maintenance_requests
            WHERE organization_id = @OrgId AND deleted_at IS NULL
            """;
        var row = await conn.QuerySingleAsync<StatsRow>(sql, new { OrgId = orgId });
        return new MaintenanceRequestStats(
            (int)row.TotalOpen, (int)row.TotalResolved, (int)row.TotalClosed,
            (int)row.SlaBreachedCount, (int)row.RecurringCount,
            row.TotalCostSum);
    }

    // -- GetLogsAsync --

    public async Task<IReadOnlyList<MaintenanceRequestLogItem>> GetLogsAsync(Guid requestId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT l.id, l.from_status, l.to_status, l.note,
                   p.full_name AS created_by_name, l.created_at
            FROM public.maintenance_request_logs l
            JOIN public.profiles p ON p.id = l.created_by
            WHERE l.maintenance_request_id = @RequestId
            ORDER BY l.created_at ASC
            """;
        var rows = await conn.QueryAsync<LogRow>(sql, new { RequestId = requestId });
        return rows.Select(r => new MaintenanceRequestLogItem(
            r.Id, r.FromStatus, r.ToStatus, r.Note,
            r.CreatedByName,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();
    }

    // -- GetCommentsAsync --

    public async Task<IReadOnlyList<MaintenanceRequestCommentItem>> GetCommentsAsync(Guid requestId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT c.id, c.content, c.photo_url,
                   p.full_name AS created_by_name, c.created_by, c.created_at
            FROM public.maintenance_request_comments c
            JOIN public.profiles p ON p.id = c.created_by
            WHERE c.maintenance_request_id = @RequestId
            ORDER BY c.created_at ASC
            """;
        var rows = await conn.QueryAsync<CommentRow>(sql, new { RequestId = requestId });
        return rows.Select(r => new MaintenanceRequestCommentItem(
            r.Id, r.Content, r.PhotoUrl,
            r.CreatedByName, r.CreatedBy,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();
    }

    // -- GetCostsAsync --

    public async Task<IReadOnlyList<MaintenanceRequestCostItem>> GetCostsAsync(Guid requestId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            SELECT c.id, c.amount, c.description, c.finance_record_id,
                   p.full_name AS created_by_name, c.created_at
            FROM public.maintenance_request_costs c
            JOIN public.profiles p ON p.id = c.created_by
            WHERE c.maintenance_request_id = @RequestId
            ORDER BY c.created_at ASC
            """;
        var rows = await conn.QueryAsync<CostRow>(sql, new { RequestId = requestId });
        return rows.Select(r => new MaintenanceRequestCostItem(
            r.Id, r.Amount, r.Description, r.FinanceRecordId,
            r.CreatedByName,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();
    }

    // -- CountRecentByUnitAndCategoryAsync --

    public async Task<int> CountRecentByUnitAndCategoryAsync(
        Guid orgId, Guid? unitId, string category, int days, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        string sql;
        object param;
        if (unitId is not null)
        {
            sql = """
                SELECT COUNT(*) FROM public.maintenance_requests
                WHERE organization_id = @OrgId
                  AND unit_id = @UnitId
                  AND category = @Category
                  AND created_at > now() - make_interval(days => @Days)
                  AND deleted_at IS NULL
                  AND status != 'cancelled'
                """;
            param = new { OrgId = orgId, UnitId = unitId, Category = category, Days = days };
        }
        else
        {
            sql = """
                SELECT COUNT(*) FROM public.maintenance_requests
                WHERE organization_id = @OrgId
                  AND unit_id IS NULL
                  AND category = @Category
                  AND created_at > now() - make_interval(days => @Days)
                  AND deleted_at IS NULL
                  AND status != 'cancelled'
                """;
            param = new { OrgId = orgId, Category = category, Days = days };
        }
        var count = await conn.QuerySingleAsync<long>(sql, param);
        return (int)count;
    }

    // -- MapToEntity helper --

    private static MaintenanceRequest MapToEntity(RequestRow r) => new()
    {
        Id = r.Id, OrganizationId = r.OrganizationId,
        Title = r.Title, Description = r.Description,
        Category = r.Category, Priority = r.Priority, Status = r.Status,
        LocationType = r.LocationType, UnitId = r.UnitId, LocationNote = r.LocationNote,
        AssigneeName = r.AssigneeName, AssigneePhone = r.AssigneePhone,
        AssigneeNote = r.AssigneeNote,
        AssignedAt = r.AssignedAt.HasValue ? new DateTimeOffset(r.AssignedAt.Value, TimeSpan.Zero) : null,
        TotalCost = r.TotalCost, IsRecurring = r.IsRecurring,
        SatisfactionRating = r.SatisfactionRating, SatisfactionComment = r.SatisfactionComment,
        RatedAt = r.RatedAt.HasValue ? new DateTimeOffset(r.RatedAt.Value, TimeSpan.Zero) : null,
        SlaDeadlineAt = r.SlaDeadlineAt.HasValue ? new DateTimeOffset(r.SlaDeadlineAt.Value, TimeSpan.Zero) : null,
        SlaBreached = r.SlaBreached,
        PhotoUrls = r.PhotoUrls,
        ReportedBy = r.ReportedBy,
        ResolvedAt = r.ResolvedAt.HasValue ? new DateTimeOffset(r.ResolvedAt.Value, TimeSpan.Zero) : null,
        ClosedAt = r.ClosedAt.HasValue ? new DateTimeOffset(r.ClosedAt.Value, TimeSpan.Zero) : null,
        CancelledAt = r.CancelledAt.HasValue ? new DateTimeOffset(r.CancelledAt.Value, TimeSpan.Zero) : null,
        CancelledBy = r.CancelledBy,
        CreatedBy = r.CreatedBy, UpdatedBy = r.UpdatedBy,
        DeletedAt = r.DeletedAt.HasValue ? new DateTimeOffset(r.DeletedAt.Value, TimeSpan.Zero) : null,
        DeletedBy = r.DeletedBy,
        CreatedAt = new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero)
    };
}
