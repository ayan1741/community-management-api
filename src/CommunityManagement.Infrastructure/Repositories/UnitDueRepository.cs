using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;
using System.Data;
using System.Text.Json;

namespace CommunityManagement.Infrastructure.Repositories;

public class UnitDueRepository : IUnitDueRepository
{
    private readonly IDbConnectionFactory _factory;

    public UnitDueRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record UnitDueListRow(
        Guid Id, Guid UnitId, string UnitNumber, string BlockName,
        string? ResidentName, string? UnitCategory,
        Guid DueTypeId, string DueTypeName,
        decimal Amount, decimal PaidAmount, decimal RemainingAmount,
        string Status, bool IsOverdue,
        DateTime CreatedAt,
        long TotalCount
    );

    private record UnitDueResidentRow(
        Guid Id, string PeriodName, DateTime DueDate,
        string DueTypeName, decimal Amount, decimal PaidAmount,
        string Status, bool IsOverdue,
        decimal? CalculatedLateFee,
        DateTime CreatedAt
    );

    private record UnitDueDetailRow(
        Guid Id, Guid PeriodId, Guid UnitId, Guid DueTypeId,
        decimal Amount, string Status, Guid CreatedBy, string? Note,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    private record AccrualUnitRow(
        Guid Id, string UnitNumber, string? UnitCategory, bool IsOccupied
    );

    private record AccrualDueTypeRow(
        Guid Id, string Name, decimal DefaultAmount, string? CategoryAmounts
    );

    public async Task<(IReadOnlyList<UnitDueListItem> Items, int TotalCount)> GetByPeriodIdAsync(
        Guid periodId, string? status, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var statusFilter = status is not null ? "AND ud.status = @Status" : "";

        var sql = $"""
            SELECT
                ud.id, ud.unit_id, u.unit_number, b.name AS block_name,
                resident.full_name AS resident_name, u.unit_category,
                ud.due_type_id, dt.name AS due_type_name,
                ud.amount,
                COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0) AS paid_amount,
                ud.amount - COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0) AS remaining_amount,
                ud.status,
                (dp.due_date < CURRENT_DATE AND ud.status IN ('pending','partial')) AS is_overdue,
                ud.created_at,
                COUNT(*) OVER() AS total_count
            FROM public.unit_dues ud
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.units u ON u.id = ud.unit_id
            JOIN public.blocks b ON b.id = u.block_id
            JOIN public.due_types dt ON dt.id = ud.due_type_id
            LEFT JOIN LATERAL (
                SELECT p.full_name
                FROM public.unit_residents ur
                JOIN public.profiles p ON p.id = ur.user_id
                WHERE ur.unit_id = ud.unit_id AND ur.status = 'active'
                ORDER BY ur.is_primary DESC NULLS LAST
                LIMIT 1
            ) resident ON true
            LEFT JOIN public.payments py ON py.unit_due_id = ud.id
            WHERE ud.period_id = @PeriodId
              {statusFilter}
            GROUP BY ud.id, u.unit_number, b.name, resident.full_name, u.unit_category,
                     dt.name, dp.due_date
            ORDER BY b.name ASC, u.unit_number ASC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<UnitDueListRow>(sql, new
        {
            PeriodId = periodId,
            Status = status,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        if (rows.Count == 0)
            return (Array.Empty<UnitDueListItem>(), 0);

        var totalCount = (int)rows[0].TotalCount;
        var items = rows.Select(r => new UnitDueListItem(
            r.Id, r.UnitId, r.UnitNumber, r.BlockName,
            r.ResidentName, r.UnitCategory,
            r.DueTypeId, r.DueTypeName,
            r.Amount, r.PaidAmount, r.RemainingAmount,
            r.Status, r.IsOverdue,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<UnitDueResidentItem>> GetByUserIdAsync(Guid userId, Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                ud.id, dp.name AS period_name, dp.due_date,
                dt.name AS due_type_name,
                ud.amount,
                COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0) AS paid_amount,
                ud.status,
                (dp.due_date < CURRENT_DATE AND ud.status IN ('pending','partial')) AS is_overdue,
                CASE
                  WHEN dp.due_date < CURRENT_DATE AND ud.status IN ('pending','partial')
                       AND s.late_fee_rate > 0
                  THEN ROUND(
                    (ud.amount - COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0))
                    * s.late_fee_rate
                    * GREATEST(0, (CURRENT_DATE - dp.due_date - s.late_fee_grace_days)::numeric / 30)
                  , 2)
                  ELSE NULL
                END AS calculated_late_fee,
                ud.created_at
            FROM public.unit_dues ud
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.due_types dt ON dt.id = ud.due_type_id
            LEFT JOIN public.organization_due_settings s ON s.organization_id = dp.organization_id
            LEFT JOIN public.payments py ON py.unit_due_id = ud.id AND py.cancelled_at IS NULL
            WHERE dp.organization_id = @OrgId
              AND ud.status != 'cancelled'
              AND EXISTS (
                SELECT 1 FROM public.unit_residents ur
                WHERE ur.unit_id = ud.unit_id AND ur.user_id = @UserId AND ur.status = 'active'
              )
            GROUP BY ud.id, dp.name, dp.due_date, dt.name, s.late_fee_rate, s.late_fee_grace_days
            ORDER BY dp.due_date DESC
            """;

        var rows = await conn.QueryAsync<UnitDueResidentRow>(sql, new { UserId = userId, OrgId = orgId });
        return rows.Select(r => new UnitDueResidentItem(
            r.Id, r.PeriodName, DateOnly.FromDateTime(r.DueDate),
            r.DueTypeName, r.Amount, r.PaidAmount,
            r.Status, r.IsOverdue, r.CalculatedLateFee,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();
    }

    public async Task<IReadOnlyList<UnitDueResidentItem>> GetByUnitIdAsync(Guid unitId, Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                ud.id, dp.name AS period_name, dp.due_date,
                dt.name AS due_type_name,
                ud.amount,
                COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0) AS paid_amount,
                ud.status,
                (dp.due_date < CURRENT_DATE AND ud.status IN ('pending','partial')) AS is_overdue,
                CASE
                  WHEN dp.due_date < CURRENT_DATE AND ud.status IN ('pending','partial')
                       AND s.late_fee_rate > 0
                  THEN ROUND(
                    (ud.amount - COALESCE(SUM(py.amount) FILTER (WHERE py.cancelled_at IS NULL), 0))
                    * s.late_fee_rate
                    * GREATEST(0, (CURRENT_DATE - dp.due_date - s.late_fee_grace_days)::numeric / 30)
                  , 2)
                  ELSE NULL
                END AS calculated_late_fee,
                ud.created_at
            FROM public.unit_dues ud
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            JOIN public.due_types dt ON dt.id = ud.due_type_id
            JOIN public.units u ON u.id = ud.unit_id
            LEFT JOIN public.organization_due_settings s ON s.organization_id = dp.organization_id
            LEFT JOIN public.payments py ON py.unit_due_id = ud.id AND py.cancelled_at IS NULL
            WHERE ud.unit_id = @UnitId
              AND dp.organization_id = @OrgId
              AND ud.status != 'cancelled'
            GROUP BY ud.id, dp.name, dp.due_date, dt.name, s.late_fee_rate, s.late_fee_grace_days
            ORDER BY dp.due_date DESC
            """;

        var rows = await conn.QueryAsync<UnitDueResidentRow>(sql, new { UnitId = unitId, OrgId = orgId });
        return rows.Select(r => new UnitDueResidentItem(
            r.Id, r.PeriodName, DateOnly.FromDateTime(r.DueDate),
            r.DueTypeName, r.Amount, r.PaidAmount,
            r.Status, r.IsOverdue, r.CalculatedLateFee,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)
        )).ToList();
    }

    public async Task<UnitDue?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, period_id, unit_id, due_type_id, amount, status, created_by, note, created_at, updated_at
            FROM public.unit_dues
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<UnitDueDetailRow>(sql, new { Id = id });
        return row is null ? null : MapDetailRow(row);
    }

    public async Task<bool> ExistsAsync(Guid periodId, Guid unitId, Guid dueTypeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.unit_dues
                WHERE period_id = @PeriodId AND unit_id = @UnitId AND due_type_id = @DueTypeId
                  AND status != 'cancelled'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { PeriodId = periodId, UnitId = unitId, DueTypeId = dueTypeId });
    }

    public async Task<AccrualPreview> GetAccrualPreviewAsync(AccrualParams parameters, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        // Daireleri ve doluluk durumlarını çek
        const string unitSql = """
            SELECT
                u.id,
                u.unit_number,
                u.unit_category,
                EXISTS(
                    SELECT 1 FROM public.unit_residents ur
                    WHERE ur.unit_id = u.id AND ur.status = 'active'
                ) AS is_occupied
            FROM public.units u
            JOIN public.blocks b ON b.id = u.block_id
            WHERE u.organization_id = @OrgId AND u.deleted_at IS NULL
            ORDER BY b.name ASC, u.unit_number ASC
            """;

        const string dueTypeSql = """
            SELECT id, name, default_amount, category_amounts
            FROM public.due_types
            WHERE id = ANY(@Ids) AND organization_id = @OrgId AND is_active = true
            """;

        var allUnits = (await conn.QueryAsync<AccrualUnitRow>(unitSql, new { OrgId = parameters.OrganizationId })).ToList();
        var dueTypes = (await conn.QueryAsync<AccrualDueTypeRow>(dueTypeSql, new
        {
            Ids = parameters.DueTypeIds.ToArray(),
            OrgId = parameters.OrganizationId
        })).ToList();

        var totalUnits = allUnits.Count;
        var occupiedUnits = allUnits.Count(u => u.IsOccupied);
        var emptyUnits = totalUnits - occupiedUnits;

        var includedUnits = parameters.IncludeEmptyUnits
            ? allUnits
            : allUnits.Where(u => u.IsOccupied).ToList();

        var unitsWithoutCategory = includedUnits.Count(u => u.UnitCategory is null);

        var breakdowns = new List<AccrualPreviewLine>();
        decimal grandTotal = 0;

        foreach (var dt in dueTypes)
        {
            var categoryAmountsMap = ParseCategoryAmounts(dt.CategoryAmounts);
            const string NoCategoryKey = "";
            var categoryGroups = new Dictionary<string, (int Count, decimal Amount)>();

            foreach (var unit in includedUnits)
            {
                var unitAmount = GetUnitAmount(unit.UnitCategory, dt.DefaultAmount, categoryAmountsMap);
                var catKey = unit.UnitCategory ?? NoCategoryKey;
                if (categoryGroups.TryGetValue(catKey, out var existing))
                    categoryGroups[catKey] = (existing.Count + 1, existing.Amount == unitAmount ? unitAmount : existing.Amount);
                else
                    categoryGroups[catKey] = (1, unitAmount);
            }

            var categoryLines = new List<AccrualCategoryLine>();
            decimal subtotal = 0;

            foreach (var (cat, (count, amount)) in categoryGroups)
            {
                var lineSubtotal = amount * count;
                categoryLines.Add(new AccrualCategoryLine(cat == NoCategoryKey ? null : cat, amount, count, lineSubtotal));
                subtotal += lineSubtotal;
            }

            grandTotal += subtotal;
            breakdowns.Add(new AccrualPreviewLine(
                dt.Id, dt.Name, categoryLines,
                categoryGroups.TryGetValue(NoCategoryKey, out var noCat) ? noCat.Count : 0,
                subtotal
            ));
        }

        return new AccrualPreview(
            totalUnits, occupiedUnits, emptyUnits, includedUnits.Count,
            breakdowns, unitsWithoutCategory, grandTotal
        );
    }

    public async Task BulkCreateAsync(IReadOnlyList<UnitDue> unitDues, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO public.unit_dues
                (id, period_id, unit_id, due_type_id, amount, status, created_by, note, created_at, updated_at)
            VALUES
                (@Id, @PeriodId, @UnitId, @DueTypeId, @Amount, @Status, @CreatedBy, @Note, @CreatedAt, @UpdatedAt)
            ON CONFLICT (period_id, unit_id, due_type_id) DO NOTHING
            """;

        foreach (var ud in unitDues)
        {
            await conn.ExecuteAsync(sql, new
            {
                ud.Id,
                ud.PeriodId,
                ud.UnitId,
                ud.DueTypeId,
                ud.Amount,
                ud.Status,
                ud.CreatedBy,
                ud.Note,
                CreatedAt = ud.CreatedAt.UtcDateTime,
                UpdatedAt = ud.UpdatedAt.UtcDateTime
            }, tx);
        }
    }

    public async Task<UnitDue> CreateAsync(UnitDue unitDue, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.unit_dues
                (id, period_id, unit_id, due_type_id, amount, status, created_by, note, created_at, updated_at)
            VALUES
                (@Id, @PeriodId, @UnitId, @DueTypeId, @Amount, @Status, @CreatedBy, @Note, @CreatedAt, @UpdatedAt)
            RETURNING id, period_id, unit_id, due_type_id, amount, status, created_by, note, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<UnitDueDetailRow>(sql, new
        {
            unitDue.Id,
            unitDue.PeriodId,
            unitDue.UnitId,
            unitDue.DueTypeId,
            unitDue.Amount,
            unitDue.Status,
            unitDue.CreatedBy,
            unitDue.Note,
            CreatedAt = unitDue.CreatedAt.UtcDateTime,
            UpdatedAt = unitDue.UpdatedAt.UtcDateTime
        });
        return MapDetailRow(row);
    }

    public async Task UpdateStatusAsync(Guid id, string status, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.unit_dues
            SET status = @Status, updated_at = now()
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, new { Id = id, Status = status });
    }

    public async Task CancelWithLateFeesAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default)
    {
        const string sql = """
            UPDATE public.unit_dues
            SET status = 'cancelled', updated_at = now()
            WHERE id = @UnitDueId
            """;
        await conn.ExecuteAsync(sql, new { UnitDueId = unitDueId }, tx);
    }

    private static UnitDue MapDetailRow(UnitDueDetailRow row) => new()
    {
        Id = row.Id,
        PeriodId = row.PeriodId,
        UnitId = row.UnitId,
        DueTypeId = row.DueTypeId,
        Amount = row.Amount,
        Status = row.Status,
        CreatedBy = row.CreatedBy,
        Note = row.Note,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };

    private static Dictionary<string, decimal> ParseCategoryAmounts(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, decimal>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, decimal>>(json)
                ?? new Dictionary<string, decimal>();
        }
        catch
        {
            return new Dictionary<string, decimal>();
        }
    }

    private static decimal GetUnitAmount(string? unitCategory, decimal defaultAmount, Dictionary<string, decimal> categoryAmounts)
    {
        if (unitCategory is not null && categoryAmounts.TryGetValue(unitCategory, out var amount))
            return amount;
        return defaultAmount;
    }
}
