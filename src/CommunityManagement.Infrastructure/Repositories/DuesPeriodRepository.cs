using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class DuesPeriodRepository : IDuesPeriodRepository
{
    private readonly IDbConnectionFactory _factory;

    public DuesPeriodRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record DuesPeriodRow(
        Guid Id, Guid OrganizationId, string Name,
        DateTime StartDate, DateTime DueDate, string Status,
        Guid CreatedBy,
        DateTime? ReminderSentAt, DateTime? ClosedAt,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    private record DuesPeriodListRow(
        Guid Id, string Name,
        DateTime StartDate, DateTime DueDate, string Status,
        long TotalDues, long PaidCount, long PendingCount,
        decimal TotalAmount, decimal CollectedAmount,
        DateTime CreatedAt
    );

    public async Task<IReadOnlyList<DuesPeriodListItem>> GetByOrgIdAsync(Guid orgId, string? status = null, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                dp.id, dp.name, dp.start_date, dp.due_date, dp.status,
                COUNT(ud.id) FILTER (WHERE ud.status != 'cancelled') AS total_dues,
                COUNT(ud.id) FILTER (WHERE ud.status = 'paid') AS paid_count,
                COUNT(ud.id) FILTER (WHERE ud.status IN ('pending','partial')) AS pending_count,
                COALESCE(SUM(ud.amount) FILTER (WHERE ud.status != 'cancelled'), 0) AS total_amount,
                COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0) AS collected_amount,
                dp.created_at
            FROM public.dues_periods dp
            LEFT JOIN public.unit_dues ud ON ud.period_id = dp.id
            LEFT JOIN public.payments py ON py.unit_due_id = ud.id
            WHERE dp.organization_id = @OrgId
              AND (@Status IS NULL OR dp.status = @Status)
            GROUP BY dp.id
            ORDER BY dp.due_date DESC
            """;

        var rows = await conn.QueryAsync<DuesPeriodListRow>(sql, new { OrgId = orgId, Status = status });
        return rows.Select(r => new DuesPeriodListItem(
            r.Id, r.Name,
            DateOnly.FromDateTime(r.StartDate), DateOnly.FromDateTime(r.DueDate),
            r.Status,
            r.TotalDues, r.PaidCount, r.PendingCount,
            r.TotalAmount, r.CollectedAmount,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();
    }

    public async Task<DuesPeriod?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, name, start_date, due_date, status, created_by,
                   reminder_sent_at, closed_at, created_at, updated_at
            FROM public.dues_periods
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DuesPeriodRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<bool> HasAccrualsAsync(Guid periodId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.unit_dues
                WHERE period_id = @PeriodId AND status != 'cancelled'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { PeriodId = periodId });
    }

    public async Task<DuesPeriod> CreateAsync(DuesPeriod period, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.dues_periods
                (id, organization_id, name, start_date, due_date, status, created_by, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @Name, @StartDate, @DueDate, @Status, @CreatedBy, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, name, start_date, due_date, status, created_by,
                      reminder_sent_at, closed_at, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<DuesPeriodRow>(sql, new
        {
            period.Id,
            period.OrganizationId,
            period.Name,
            StartDate = period.StartDate.ToDateTime(TimeOnly.MinValue),
            DueDate = period.DueDate.ToDateTime(TimeOnly.MinValue),
            period.Status,
            period.CreatedBy,
            CreatedAt = period.CreatedAt.UtcDateTime,
            UpdatedAt = period.UpdatedAt.UtcDateTime
        });
        return MapRow(row);
    }

    public async Task UpdateStatusAsync(Guid periodId, string status, DateTimeOffset? closedAt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.dues_periods
            SET status = @Status,
                closed_at = @ClosedAt,
                updated_at = now()
            WHERE id = @PeriodId
            """;
        await conn.ExecuteAsync(sql, new
        {
            PeriodId = periodId,
            Status = status,
            ClosedAt = closedAt?.UtcDateTime
        });
    }

    public async Task DeleteAsync(Guid periodId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            DELETE FROM public.dues_periods
            WHERE id = @PeriodId AND status = 'draft'
            """;
        await conn.ExecuteAsync(sql, new { PeriodId = periodId });
    }

    private static DuesPeriod MapRow(DuesPeriodRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        Name = row.Name,
        StartDate = DateOnly.FromDateTime(row.StartDate),
        DueDate = DateOnly.FromDateTime(row.DueDate),
        Status = row.Status,
        CreatedBy = row.CreatedBy,
        ReminderSentAt = row.ReminderSentAt.HasValue ? new DateTimeOffset(row.ReminderSentAt.Value, TimeSpan.Zero) : null,
        ClosedAt = row.ClosedAt.HasValue ? new DateTimeOffset(row.ClosedAt.Value, TimeSpan.Zero) : null,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
