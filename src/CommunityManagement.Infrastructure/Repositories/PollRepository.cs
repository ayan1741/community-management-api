using CommunityManagement.Core.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class PollRepository : IPollRepository
{
    private readonly IDbConnectionFactory _factory;
    public PollRepository(IDbConnectionFactory factory) => _factory = factory;

    // -- Private Row Records --

    private record PollRow(
        Guid Id, Guid OrganizationId, Guid? AgendaItemId, Guid CreatedBy,
        string Title, string? Description, string PollType,
        DateTime StartsAt, DateTime EndsAt,
        string Status, bool ShowInterimResults, int TotalMemberCount,
        DateTime CreatedAt, DateTime UpdatedAt);

    private record ListRow(
        Guid Id, string Title, string PollType,
        string Status, DateTime StartsAt, DateTime EndsAt,
        int TotalVoteCount, int TotalMemberCount,
        bool HasUserVoted,
        string? AgendaItemTitle,
        DateTime CreatedAt, long TotalCount);

    private record DetailRow(
        Guid Id, Guid OrganizationId, Guid? AgendaItemId,
        string Title, string Description, string PollType,
        string Status, DateTime StartsAt, DateTime EndsAt,
        bool ShowInterimResults,
        int TotalMemberCount,
        string CreatedByName,
        DateTime CreatedAt);

    private record OptionRow(Guid Id, string Label, int VoteCount, short DisplayOrder);

    // -- GetByIdAsync --

    public async Task<Poll?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, agenda_item_id, created_by,
                   title, description, poll_type,
                   starts_at, ends_at,
                   status, show_interim_results, total_member_count,
                   created_at, updated_at
            FROM public.polls
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<PollRow>(sql, new { Id = id });
        if (row is null) return null;

        return new Poll
        {
            Id = row.Id, OrganizationId = row.OrganizationId,
            AgendaItemId = row.AgendaItemId, CreatedBy = row.CreatedBy,
            Title = row.Title, Description = row.Description,
            PollType = row.PollType,
            StartsAt = new DateTimeOffset(row.StartsAt, TimeSpan.Zero),
            EndsAt = new DateTimeOffset(row.EndsAt, TimeSpan.Zero),
            Status = row.Status, ShowInterimResults = row.ShowInterimResults,
            TotalMemberCount = row.TotalMemberCount,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    // -- GetDetailAsync --

    public async Task<PollDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT p.id, p.organization_id, p.agenda_item_id,
                   p.title, COALESCE(p.description, '') AS description, p.poll_type,
                   p.status, p.starts_at, p.ends_at,
                   p.show_interim_results, p.total_member_count,
                   pr.full_name AS created_by_name,
                   p.created_at
            FROM public.polls p
            JOIN public.profiles pr ON pr.id = p.created_by
            WHERE p.id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DetailRow>(sql, new { Id = id });
        if (row is null) return null;

        return new PollDetailDto(
            row.Id, row.OrganizationId, row.AgendaItemId,
            row.Title, row.Description, row.PollType,
            row.Status,
            new DateTimeOffset(row.StartsAt, TimeSpan.Zero),
            new DateTimeOffset(row.EndsAt, TimeSpan.Zero),
            row.ShowInterimResults, row.TotalMemberCount,
            row.CreatedByName,
            new DateTimeOffset(row.CreatedAt, TimeSpan.Zero));
    }

    // -- GetListAsync --

    public async Task<(IReadOnlyList<PollListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId,
        string? status,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var sql = """
            SELECT p.id, p.title, p.poll_type,
                   p.status, p.starts_at, p.ends_at,
                   (SELECT COALESCE(SUM(po.vote_count), 0) FROM public.poll_options po WHERE po.poll_id = p.id)::int AS total_vote_count,
                   p.total_member_count,
                   EXISTS(SELECT 1 FROM public.poll_votes pv WHERE pv.poll_id = p.id AND pv.user_id = @CurrentUserId) AS has_user_voted,
                   ai.title AS agenda_item_title,
                   p.created_at,
                   COUNT(*) OVER() AS total_count
            FROM public.polls p
            LEFT JOIN public.agenda_items ai ON ai.id = p.agenda_item_id
            WHERE p.organization_id = @OrgId
            """;

        if (!string.IsNullOrEmpty(status)) sql += " AND p.status = @Status";

        sql += " ORDER BY p.created_at DESC LIMIT @PageSize OFFSET @Offset";

        var rows = await conn.QueryAsync<ListRow>(sql, new
        {
            OrgId = orgId, CurrentUserId = currentUserId,
            Status = status,
            PageSize = pageSize, Offset = (page - 1) * pageSize
        });

        var list = rows.Select(r => new PollListDto(
            r.Id, r.Title, r.PollType,
            r.Status,
            new DateTimeOffset(r.StartsAt, TimeSpan.Zero),
            new DateTimeOffset(r.EndsAt, TimeSpan.Zero),
            r.TotalVoteCount, r.TotalMemberCount,
            r.HasUserVoted,
            r.AgendaItemTitle,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            r.TotalCount)).ToList();

        var totalCount = list.Count > 0 ? (int)list[0].TotalCount : 0;
        return (list, totalCount);
    }

    // -- GetOptionsAsync --

    public async Task<IReadOnlyList<PollOptionDto>> GetOptionsAsync(Guid pollId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, label, vote_count, display_order
            FROM public.poll_options
            WHERE poll_id = @PollId
            ORDER BY display_order ASC
            """;
        var rows = await conn.QueryAsync<OptionRow>(sql, new { PollId = pollId });
        return rows.Select(r => new PollOptionDto(r.Id, r.Label, r.VoteCount, r.DisplayOrder)).ToList();
    }

    // -- GetUserVoteAsync --

    public async Task<UserVoteDto?> GetUserVoteAsync(Guid pollId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        var optionId = await conn.QuerySingleOrDefaultAsync<Guid?>(
            "SELECT poll_option_id FROM public.poll_votes WHERE poll_id = @PollId AND user_id = @UserId",
            new { PollId = pollId, UserId = userId });
        return optionId.HasValue ? new UserVoteDto(optionId.Value) : null;
    }

    // -- GetResultAsync --

    public async Task<PollResultDto?> GetResultAsync(Guid pollId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var poll = await GetByIdAsync(pollId, ct);
        if (poll is null) return null;

        var options = await GetOptionsAsync(pollId, ct);
        var totalVotes = options.Sum(o => o.VoteCount);

        return new PollResultDto(
            poll.Id, poll.Title, poll.PollType, poll.Status,
            totalVotes, poll.TotalMemberCount, options);
    }

    // -- GetTotalVoteCountAsync --

    public async Task<int> GetTotalVoteCountAsync(Guid pollId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var count = await conn.QuerySingleAsync<long>(
            "SELECT COALESCE(SUM(vote_count), 0) FROM public.poll_options WHERE poll_id = @PollId",
            new { PollId = pollId });
        return (int)count;
    }
}
