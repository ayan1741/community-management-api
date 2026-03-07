using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;
using System.Text;

namespace CommunityManagement.Infrastructure.Repositories;

public class MeetingRepository : IMeetingRepository
{
    private readonly IDbConnectionFactory _factory;
    public MeetingRepository(IDbConnectionFactory factory) => _factory = factory;

    // -- Private Row Records --

    private record MeetingRow(
        Guid Id, Guid OrganizationId, string Title, string? Description,
        DateTime MeetingDate, string Status, Guid CreatedBy,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record ListRow(
        Guid Id, string Title, string Status,
        DateTime MeetingDate,
        int AgendaItemCount,
        DateTime CreatedAt, long TotalCount);

    private record DetailRow(
        Guid Id, Guid OrganizationId,
        string Title, string Description, string Status,
        DateTime MeetingDate,
        string CreatedByName,
        DateTime CreatedAt, DateTime UpdatedAt);

    // -- GetByIdAsync --

    public async Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, title, description,
                   meeting_date, status, created_by,
                   created_at, updated_at
            FROM public.meetings
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<MeetingRow>(sql, new { Id = id });
        if (row is null) return null;

        return new Meeting
        {
            Id = row.Id, OrganizationId = row.OrganizationId,
            Title = row.Title, Description = row.Description,
            MeetingDate = new DateTimeOffset(row.MeetingDate, TimeSpan.Zero),
            Status = row.Status, CreatedBy = row.CreatedBy,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    // -- GetDetailAsync --

    public async Task<MeetingDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT m.id, m.organization_id,
                   m.title, COALESCE(m.description, '') AS description, m.status,
                   m.meeting_date,
                   p.full_name AS created_by_name,
                   m.created_at, m.updated_at
            FROM public.meetings m
            JOIN public.profiles p ON p.id = m.created_by
            WHERE m.id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id });
        if (row is null) return null;

        return new MeetingDetailDto(
            row.Id, row.OrganizationId,
            row.Title, row.Description, row.Status,
            new DateTimeOffset(row.MeetingDate, TimeSpan.Zero),
            row.CreatedByName,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero));
    }

    // -- GetListAsync --

    public async Task<(IReadOnlyList<MeetingListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sb = new StringBuilder();
        sb.Append("""
            SELECT m.id, m.title, m.status,
                   m.meeting_date,
                   (SELECT COUNT(*)::int FROM public.agenda_items ai
                    WHERE ai.meeting_id = m.id AND ai.deleted_at IS NULL) AS agenda_item_count,
                   m.created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.meetings m
            WHERE m.organization_id = @OrgId
            """);

        if (!string.IsNullOrEmpty(status)) sb.Append(" AND m.status = @Status");

        sb.Append(" ORDER BY m.meeting_date DESC LIMIT @PageSize OFFSET @Offset");

        var rows = await conn.QueryAsync<ListRow>(sb.ToString(), new
        {
            OrgId = orgId, Status = status,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        });

        var list = rows.Select(r => new MeetingListDto(
            r.Id, r.Title, r.Status,
            new DateTimeOffset(r.MeetingDate, TimeSpan.Zero),
            r.AgendaItemCount,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            r.TotalCount)).ToList();

        var totalCount = list.Count > 0 ? (int)list[0].TotalCount : 0;
        return (list, totalCount);
    }
}
