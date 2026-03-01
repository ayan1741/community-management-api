using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;
using System.Data;

namespace CommunityManagement.Infrastructure.Repositories;

public class LateFeeRepository : ILateFeeRepository
{
    private readonly IDbConnectionFactory _factory;

    public LateFeeRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record LateFeeRow(
        Guid Id, Guid UnitDueId, decimal Amount, decimal Rate, int DaysOverdue,
        DateTime AppliedAt, Guid AppliedBy, string Status,
        DateTime? CancelledAt, Guid? CancelledBy, string? Note
    );

    public async Task<IReadOnlyList<LateFee>> GetByUnitDueIdAsync(Guid unitDueId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, unit_due_id, amount, rate, days_overdue, applied_at, applied_by,
                   status, cancelled_at, cancelled_by, note
            FROM public.late_fees
            WHERE unit_due_id = @UnitDueId
            ORDER BY applied_at DESC
            """;
        var rows = await conn.QueryAsync<LateFeeRow>(sql, new { UnitDueId = unitDueId });
        return rows.Select(MapRow).ToList();
    }

    public async Task<bool> HasActiveLateFeeAsync(Guid unitDueId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.late_fees
                WHERE unit_due_id = @UnitDueId AND status = 'active'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UnitDueId = unitDueId });
    }

    public async Task<LateFee> CreateAsync(LateFee lateFee, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.late_fees
                (id, unit_due_id, amount, rate, days_overdue, applied_at, applied_by, status, note)
            VALUES
                (@Id, @UnitDueId, @Amount, @Rate, @DaysOverdue, @AppliedAt, @AppliedBy, @Status, @Note)
            RETURNING id, unit_due_id, amount, rate, days_overdue, applied_at, applied_by,
                      status, cancelled_at, cancelled_by, note
            """;
        var row = await conn.QuerySingleAsync<LateFeeRow>(sql, new
        {
            lateFee.Id,
            lateFee.UnitDueId,
            lateFee.Amount,
            lateFee.Rate,
            lateFee.DaysOverdue,
            AppliedAt = lateFee.AppliedAt.UtcDateTime,
            lateFee.AppliedBy,
            lateFee.Status,
            lateFee.Note
        });
        return MapRow(row);
    }

    public async Task CancelAsync(Guid id, Guid cancelledBy, string note, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.late_fees
            SET status = 'cancelled', cancelled_at = now(), cancelled_by = @CancelledBy, note = @Note
            WHERE id = @Id AND status = 'active'
            """;
        await conn.ExecuteAsync(sql, new { Id = id, CancelledBy = cancelledBy, Note = note });
    }

    public async Task CancelByUnitDueIdAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE public.late_fees
            SET status = 'cancelled', cancelled_at = now(), cancelled_by = @CancelledBy
            WHERE unit_due_id = @UnitDueId AND status = 'active'
            """;
        await conn.ExecuteAsync(sql, new { UnitDueId = unitDueId, CancelledBy = cancelledBy }, tx);
    }

    private static LateFee MapRow(LateFeeRow row) => new()
    {
        Id = row.Id,
        UnitDueId = row.UnitDueId,
        Amount = row.Amount,
        Rate = row.Rate,
        DaysOverdue = row.DaysOverdue,
        AppliedAt = new DateTimeOffset(row.AppliedAt, TimeSpan.Zero),
        AppliedBy = row.AppliedBy,
        Status = row.Status,
        CancelledAt = row.CancelledAt.HasValue ? new DateTimeOffset(row.CancelledAt.Value, TimeSpan.Zero) : null,
        CancelledBy = row.CancelledBy,
        Note = row.Note
    };
}
