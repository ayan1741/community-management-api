using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class DecisionRepository : IDecisionRepository
{
    private readonly IDbConnectionFactory _factory;
    public DecisionRepository(IDbConnectionFactory factory) => _factory = factory;

    // -- Private Row Records --

    private record DecisionRow(
        Guid Id, Guid OrganizationId, Guid? AgendaItemId, Guid? PollId,
        Guid DecidedBy, string Title, string? Description,
        string Status, DateTime DecidedAt,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record ListRow(
        Guid Id, string Title, string Status,
        string? AgendaItemTitle, string? PollTitle,
        string DecidedByName,
        DateTime DecidedAt, long TotalCount);

    private record DetailRow(
        Guid Id, Guid OrganizationId,
        Guid? AgendaItemId, string? AgendaItemTitle,
        Guid? PollId, string? PollTitle,
        string Title, string Description, string Status,
        string DecidedByName,
        DateTime DecidedAt,
        DateTime CreatedAt, DateTime UpdatedAt);

    // -- GetByIdAsync --

    public async Task<Decision?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, agenda_item_id, poll_id,
                   decided_by, title, description, status, decided_at,
                   created_at, updated_at
            FROM public.decisions
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DecisionRow>(sql, new { Id = id });
        if (row is null) return null;

        return new Decision
        {
            Id = row.Id, OrganizationId = row.OrganizationId,
            AgendaItemId = row.AgendaItemId, PollId = row.PollId,
            DecidedBy = row.DecidedBy, Title = row.Title, Description = row.Description,
            Status = row.Status,
            DecidedAt = new DateTimeOffset(row.DecidedAt, TimeSpan.Zero),
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    // -- GetDetailAsync --

    public async Task<DecisionDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT d.id, d.organization_id,
                   d.agenda_item_id, ai.title AS agenda_item_title,
                   d.poll_id, p.title AS poll_title,
                   d.title, COALESCE(d.description, '') AS description, d.status,
                   pr.full_name AS decided_by_name,
                   d.decided_at,
                   d.created_at, d.updated_at
            FROM public.decisions d
            JOIN public.profiles pr ON pr.id = d.decided_by
            LEFT JOIN public.agenda_items ai ON ai.id = d.agenda_item_id
            LEFT JOIN public.polls p ON p.id = d.poll_id
            WHERE d.id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id });
        if (row is null) return null;

        return new DecisionDetailDto(
            row.Id, row.OrganizationId,
            row.AgendaItemId, row.AgendaItemTitle,
            row.PollId, row.PollTitle,
            row.Title, row.Description, row.Status,
            row.DecidedByName,
            new DateTimeOffset(row.DecidedAt, TimeSpan.Zero),
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    // -- GetListAsync --

    public async Task<(IReadOnlyList<DecisionListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, string? status,
        DateTimeOffset? fromDate, DateTimeOffset? toDate,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder();
        sb.Append("""
            SELECT d.id, d.title, d.status,
                   ai.title AS agenda_item_title,
                   p.title AS poll_title,
                   pr.full_name AS decided_by_name,
                   d.decided_at,
                   COUNT(*) OVER() AS total_count
            FROM public.decisions d
            JOIN public.profiles pr ON pr.id = d.decided_by
            LEFT JOIN public.agenda_items ai ON ai.id = d.agenda_item_id
            LEFT JOIN public.polls p ON p.id = d.poll_id
            WHERE d.organization_id = @OrgId
            """);

        if (!string.IsNullOrEmpty(status)) sb.Append(" AND d.status = @Status");
        if (fromDate.HasValue) sb.Append(" AND d.decided_at >= @FromDate");
        if (toDate.HasValue) sb.Append(" AND d.decided_at <= @ToDate");

        sb.Append(" ORDER BY d.decided_at DESC LIMIT @PageSize OFFSET @Offset");

        var rows = await conn.QueryAsync<ListRow>(sb.ToString(), new
        {
            OrgId = orgId, Status = status,
            FromDate = fromDate?.UtcDateTime,
            ToDate = toDate?.UtcDateTime,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        });

        var list = rows.Select(r => new DecisionListDto(
            r.Id, r.Title, r.Status,
            r.AgendaItemTitle, r.PollTitle,
            r.DecidedByName,
            new DateTimeOffset(r.DecidedAt, TimeSpan.Zero),
            r.TotalCount)).ToList();

        var totalCount = list.Count > 0 ? (int)list[0].TotalCount : 0;
        return (list, totalCount);
    }
}
