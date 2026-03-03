using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class FinanceRecordRepository : IFinanceRecordRepository
{
    private readonly IDbConnectionFactory _factory;

    public FinanceRecordRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    // --- Row Records (Npgsql 9.x: timestamptz → DateTime, date → DateTime, COUNT → long) ---

    private record RecordRow(
        Guid Id, Guid OrganizationId, Guid CategoryId, string Type,
        decimal Amount, DateTime RecordDate, string Description,
        int PeriodYear, int PeriodMonth,
        string? PaymentMethod, string? DocumentUrl, bool IsOpeningBalance,
        Guid CreatedBy, Guid? UpdatedBy, DateTime? DeletedAt, Guid? DeletedBy,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    private record RecordListRow(
        Guid Id, Guid CategoryId, string CategoryName, string? CategoryIcon,
        string Type, decimal Amount, DateTime RecordDate,
        int PeriodYear, int PeriodMonth,
        string Description, string? PaymentMethod, string? DocumentUrl,
        bool IsOpeningBalance, string CreatedByName,
        DateTime CreatedAt, long TotalCount
    );

    private record TotalsRow(decimal TotalIncome, decimal TotalExpense);

    private record MonthTotalsRow(int Month, decimal TotalIncome, decimal TotalExpense);

    private record BreakdownRow(
        Guid CategoryId, string CategoryName, string? CategoryIcon, string? ParentCategoryName,
        decimal Amount
    );

    private record TrendRow(int Year, int Month, decimal Amount);
    private record MonthDecimalRow(int Month, decimal Amount);

    // --- reportBasis Filter Helpers ---

    private static string FinanceRecordPeriodFilter(string reportBasis)
        => reportBasis == "period"
            ? "fr.period_year = @Year AND fr.period_month = @Month"
            : "EXTRACT(YEAR FROM fr.record_date) = @Year AND EXTRACT(MONTH FROM fr.record_date) = @Month";

    private static string FinanceRecordAnnualFilter(string reportBasis)
        => reportBasis == "period"
            ? "fr.period_year = @Year"
            : "EXTRACT(YEAR FROM fr.record_date) = @Year";

    private static string FinanceRecordAnnualGroupBy(string reportBasis)
        => reportBasis == "period"
            ? "fr.period_month"
            : "EXTRACT(MONTH FROM fr.record_date)";

    private static string DuesCollectedPeriodFilter(string reportBasis)
        => reportBasis == "period"
            ? "EXTRACT(YEAR FROM dp.start_date) = @Year AND EXTRACT(MONTH FROM dp.start_date) = @Month"
            : "p.paid_at >= make_date(@Year, @Month, 1)::timestamptz AND p.paid_at < (make_date(@Year, @Month, 1) + interval '1 month')::timestamptz";

    private static string DuesCollectedAnnualFilter(string reportBasis)
        => reportBasis == "period"
            ? "EXTRACT(YEAR FROM dp.start_date) = @Year"
            : "p.paid_at >= make_date(@Year, 1, 1)::timestamptz AND p.paid_at < make_date(@Year + 1, 1, 1)::timestamptz";

    private static string DuesCollectedAnnualGroupBy(string reportBasis)
        => reportBasis == "period"
            ? "EXTRACT(MONTH FROM dp.start_date)"
            : "EXTRACT(MONTH FROM p.paid_at)";

    // --- Public Methods ---

    public async Task<FinanceRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            SELECT id, organization_id, category_id, type, amount, record_date, description,
                   period_year, period_month,
                   payment_method, document_url, is_opening_balance,
                   created_by, updated_by, deleted_at, deleted_by, created_at, updated_at
            FROM public.finance_records
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<RecordRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<(IReadOnlyList<FinanceRecordListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, string? type, Guid? categoryId,
        DateOnly? startDate, DateOnly? endDate,
        int? periodYear, int? periodMonth,
        int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        var typeFilter = type is not null ? "AND fr.type = @Type" : "";
        var categoryFilter = categoryId.HasValue ? "AND fr.category_id = @CategoryId" : "";
        var startFilter = startDate.HasValue ? "AND fr.record_date >= @StartDate" : "";
        var endFilter = endDate.HasValue ? "AND fr.record_date <= @EndDate" : "";
        var periodYearFilter = periodYear.HasValue ? "AND fr.period_year = @PeriodYear" : "";
        var periodMonthFilter = periodMonth.HasValue ? "AND fr.period_month = @PeriodMonth" : "";

        var sql = $"""
            SELECT
              fr.id, fr.category_id, fc.name AS category_name, fc.icon AS category_icon,
              fr.type, fr.amount, fr.record_date,
              fr.period_year, fr.period_month,
              fr.description,
              fr.payment_method, fr.document_url, fr.is_opening_balance,
              p.full_name AS created_by_name,
              fr.created_at,
              COUNT(*) OVER() AS total_count
            FROM public.finance_records fr
            JOIN public.finance_categories fc ON fc.id = fr.category_id
            JOIN public.profiles p ON p.id = fr.created_by
            WHERE fr.organization_id = @OrgId
              AND fr.deleted_at IS NULL
              {typeFilter}
              {categoryFilter}
              {startFilter}
              {endFilter}
              {periodYearFilter}
              {periodMonthFilter}
            ORDER BY fr.record_date DESC, fr.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<RecordListRow>(sql, new
        {
            OrgId = orgId,
            Type = type,
            CategoryId = categoryId,
            StartDate = startDate?.ToDateTime(TimeOnly.MinValue),
            EndDate = endDate?.ToDateTime(TimeOnly.MinValue),
            PeriodYear = periodYear,
            PeriodMonth = periodMonth,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        if (rows.Count == 0)
            return (Array.Empty<FinanceRecordListItem>(), 0);

        var totalCount = (int)rows[0].TotalCount;
        var items = rows.Select(r => new FinanceRecordListItem(
            r.Id, r.CategoryId, r.CategoryName, r.CategoryIcon,
            r.Type, r.Amount, DateOnly.FromDateTime(r.RecordDate),
            r.PeriodYear, r.PeriodMonth,
            r.Description, r.PaymentMethod, r.DocumentUrl,
            r.IsOpeningBalance, r.CreatedByName,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
            totalCount
        )).ToList();

        return (items, totalCount);
    }

    public async Task<MonthlyFinanceTotals> GetMonthlyTotalsAsync(
        Guid orgId, int year, int month, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var filter = FinanceRecordPeriodFilter(reportBasis);
        var sql = $"""
            SELECT
              COALESCE(SUM(CASE WHEN type = 'income' THEN amount ELSE 0 END), 0) AS total_income,
              COALESCE(SUM(CASE WHEN type = 'expense' THEN amount ELSE 0 END), 0) AS total_expense
            FROM public.finance_records fr
            WHERE fr.organization_id = @OrgId
              AND fr.deleted_at IS NULL
              AND {filter}
            """;

        var row = await conn.QuerySingleAsync<TotalsRow>(sql, new { OrgId = orgId, Year = year, Month = month });
        return new MonthlyFinanceTotals(year, month, row.TotalIncome, row.TotalExpense, row.TotalIncome - row.TotalExpense);
    }

    public async Task<IReadOnlyList<MonthlyFinanceTotals>> GetAnnualTotalsAsync(
        Guid orgId, int year, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var annualFilter = FinanceRecordAnnualFilter(reportBasis);
        var groupBy = FinanceRecordAnnualGroupBy(reportBasis);
        var monthCol = reportBasis == "period" ? "fr.period_month" : "EXTRACT(MONTH FROM fr.record_date)::int";
        var sql = $"""
            SELECT
              {monthCol} AS month,
              COALESCE(SUM(CASE WHEN type = 'income' THEN amount ELSE 0 END), 0) AS total_income,
              COALESCE(SUM(CASE WHEN type = 'expense' THEN amount ELSE 0 END), 0) AS total_expense
            FROM public.finance_records fr
            WHERE fr.organization_id = @OrgId
              AND fr.deleted_at IS NULL
              AND {annualFilter}
            GROUP BY {groupBy}
            ORDER BY month
            """;

        var rows = await conn.QueryAsync<MonthTotalsRow>(sql, new { OrgId = orgId, Year = year });
        return rows.Select(r => new MonthlyFinanceTotals(
            year, r.Month, r.TotalIncome, r.TotalExpense, r.TotalIncome - r.TotalExpense
        )).ToList();
    }

    public async Task<IReadOnlyList<CategoryBreakdownItem>> GetCategoryBreakdownAsync(
        Guid orgId, string type, int year, int month, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var filter = FinanceRecordPeriodFilter(reportBasis);
        var sql = $"""
            SELECT
              fc.id AS category_id,
              fc.name AS category_name,
              fc.icon AS category_icon,
              parent.name AS parent_category_name,
              SUM(fr.amount) AS amount
            FROM public.finance_records fr
            JOIN public.finance_categories fc ON fc.id = fr.category_id
            LEFT JOIN public.finance_categories parent ON parent.id = fc.parent_id
            WHERE fr.organization_id = @OrgId
              AND fr.deleted_at IS NULL
              AND fr.type = @Type
              AND {filter}
            GROUP BY fc.id, fc.name, fc.icon, parent.name
            ORDER BY SUM(fr.amount) DESC
            """;

        var rows = (await conn.QueryAsync<BreakdownRow>(sql, new { OrgId = orgId, Type = type, Year = year, Month = month })).ToList();

        var total = rows.Sum(r => r.Amount);
        return rows.Select(r => new CategoryBreakdownItem(
            r.CategoryId, r.CategoryName, r.CategoryIcon, r.ParentCategoryName,
            r.Amount,
            total > 0 ? Math.Round(r.Amount / total * 100, 1) : 0
        )).ToList();
    }

    public async Task<IReadOnlyList<MonthAmountItem>> GetExpenseTrendAsync(
        Guid orgId, int months, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        if (reportBasis == "period")
        {
            var now = DateTime.UtcNow;
            var startYear = now.AddMonths(-months).Year;
            var startMonth = now.AddMonths(-months).Month;
            var sql = """
                SELECT fr.period_year AS year, fr.period_month AS month, COALESCE(SUM(fr.amount), 0) AS amount
                FROM public.finance_records fr
                WHERE fr.organization_id = @OrgId
                  AND fr.deleted_at IS NULL
                  AND fr.type = 'expense'
                  AND (fr.period_year > @StartYear OR (fr.period_year = @StartYear AND fr.period_month >= @StartMonth))
                GROUP BY fr.period_year, fr.period_month
                ORDER BY fr.period_year, fr.period_month
                """;
            var rows = await conn.QueryAsync<TrendRow>(sql, new { OrgId = orgId, StartYear = startYear, StartMonth = startMonth });
            return rows.Select(r => new MonthAmountItem(r.Year, r.Month, r.Amount)).ToList();
        }
        else
        {
            var sql = """
                SELECT EXTRACT(YEAR FROM record_date)::int AS year, EXTRACT(MONTH FROM record_date)::int AS month,
                       COALESCE(SUM(amount), 0) AS amount
                FROM public.finance_records
                WHERE organization_id = @OrgId AND deleted_at IS NULL AND type = 'expense'
                  AND record_date >= (CURRENT_DATE - make_interval(months => @Months))
                GROUP BY EXTRACT(YEAR FROM record_date), EXTRACT(MONTH FROM record_date)
                ORDER BY year, month
                """;
            var rows = await conn.QueryAsync<TrendRow>(sql, new { OrgId = orgId, Months = months });
            return rows.Select(r => new MonthAmountItem(r.Year, r.Month, r.Amount)).ToList();
        }
    }

    public async Task<FinanceRecord?> GetOpeningBalanceAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            SELECT id, organization_id, category_id, type, amount, record_date, description,
                   period_year, period_month,
                   payment_method, document_url, is_opening_balance,
                   created_by, updated_by, deleted_at, deleted_by, created_at, updated_at
            FROM public.finance_records
            WHERE organization_id = @OrgId
              AND is_opening_balance = true
              AND deleted_at IS NULL
            """;
        var row = await conn.QuerySingleOrDefaultAsync<RecordRow>(sql, new { OrgId = orgId });
        return row is null ? null : MapRow(row);
    }

    public async Task<bool> HasOpeningBalanceAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.finance_records
                WHERE organization_id = @OrgId
                  AND is_opening_balance = true
                  AND deleted_at IS NULL
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { OrgId = orgId });
    }

    public async Task<decimal> GetDuesCollectedAsync(
        Guid orgId, int year, int month, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var filter = DuesCollectedPeriodFilter(reportBasis);
        var sql = $"""
            SELECT COALESCE(SUM(p.amount), 0)
            FROM public.payments p
            JOIN public.unit_dues ud ON ud.id = p.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            WHERE dp.organization_id = @OrgId
              AND p.cancelled_at IS NULL
              AND {filter}
            """;
        return await conn.QuerySingleAsync<decimal>(sql, new { OrgId = orgId, Year = year, Month = month });
    }

    public async Task<IReadOnlyList<decimal>> GetAnnualDuesCollectedAsync(
        Guid orgId, int year, string reportBasis, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var filter = DuesCollectedAnnualFilter(reportBasis);
        var groupBy = DuesCollectedAnnualGroupBy(reportBasis);
        var monthCol = reportBasis == "period"
            ? "EXTRACT(MONTH FROM dp.start_date)::int"
            : "EXTRACT(MONTH FROM p.paid_at)::int";
        var sql = $"""
            SELECT {monthCol} AS month, COALESCE(SUM(p.amount), 0) AS amount
            FROM public.payments p
            JOIN public.unit_dues ud ON ud.id = p.unit_due_id
            JOIN public.dues_periods dp ON dp.id = ud.period_id
            WHERE dp.organization_id = @OrgId
              AND p.cancelled_at IS NULL
              AND {filter}
            GROUP BY {groupBy}
            ORDER BY month
            """;

        var rows = await conn.QueryAsync<MonthDecimalRow>(sql, new { OrgId = orgId, Year = year });

        // 12 aylik dizi olustur (0-indexed degil, 1-12 arasi ay numarasi)
        var result = new decimal[12];
        foreach (var r in rows)
            result[r.Month - 1] = r.Amount;

        return result;
    }

    public async Task<FinanceRecord> CreateAsync(FinanceRecord record, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.finance_records
                (id, organization_id, category_id, type, amount, record_date, description,
                 period_year, period_month,
                 payment_method, document_url, is_opening_balance,
                 created_by, updated_by, deleted_at, deleted_by, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @CategoryId, @Type, @Amount, @RecordDate, @Description,
                 @PeriodYear, @PeriodMonth,
                 @PaymentMethod, @DocumentUrl, @IsOpeningBalance,
                 @CreatedBy, @UpdatedBy, @DeletedAt, @DeletedBy, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, category_id, type, amount, record_date, description,
                      period_year, period_month,
                      payment_method, document_url, is_opening_balance,
                      created_by, updated_by, deleted_at, deleted_by, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<RecordRow>(sql, new
        {
            record.Id,
            record.OrganizationId,
            record.CategoryId,
            record.Type,
            record.Amount,
            RecordDate = record.RecordDate.ToDateTime(TimeOnly.MinValue),
            record.Description,
            record.PeriodYear,
            record.PeriodMonth,
            record.PaymentMethod,
            record.DocumentUrl,
            record.IsOpeningBalance,
            record.CreatedBy,
            record.UpdatedBy,
            DeletedAt = record.DeletedAt?.UtcDateTime,
            record.DeletedBy,
            CreatedAt = record.CreatedAt.UtcDateTime,
            UpdatedAt = record.UpdatedAt.UtcDateTime
        });
        return MapRow(row);
    }

    public async Task UpdateAsync(FinanceRecord record, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.finance_records
            SET category_id = @CategoryId, amount = @Amount, record_date = @RecordDate,
                description = @Description, payment_method = @PaymentMethod,
                period_year = @PeriodYear, period_month = @PeriodMonth,
                document_url = @DocumentUrl, updated_by = @UpdatedBy, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, new
        {
            record.Id,
            record.CategoryId,
            record.Amount,
            RecordDate = record.RecordDate.ToDateTime(TimeOnly.MinValue),
            record.Description,
            record.PaymentMethod,
            record.PeriodYear,
            record.PeriodMonth,
            record.DocumentUrl,
            record.UpdatedBy,
            UpdatedAt = record.UpdatedAt.UtcDateTime
        });
    }

    public async Task SoftDeleteAsync(Guid id, Guid deletedBy, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.finance_records
            SET deleted_at = now(), deleted_by = @DeletedBy, updated_at = now()
            WHERE id = @Id AND deleted_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new { Id = id, DeletedBy = deletedBy });
    }

    private static FinanceRecord MapRow(RecordRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        CategoryId = row.CategoryId,
        Type = row.Type,
        Amount = row.Amount,
        RecordDate = DateOnly.FromDateTime(row.RecordDate),
        PeriodYear = row.PeriodYear,
        PeriodMonth = row.PeriodMonth,
        Description = row.Description,
        PaymentMethod = row.PaymentMethod,
        DocumentUrl = row.DocumentUrl,
        IsOpeningBalance = row.IsOpeningBalance,
        CreatedBy = row.CreatedBy,
        UpdatedBy = row.UpdatedBy,
        DeletedAt = row.DeletedAt.HasValue ? new DateTimeOffset(row.DeletedAt.Value, TimeSpan.Zero) : null,
        DeletedBy = row.DeletedBy,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
