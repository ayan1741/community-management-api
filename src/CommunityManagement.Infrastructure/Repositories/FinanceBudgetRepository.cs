using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class FinanceBudgetRepository : IFinanceBudgetRepository
{
    private readonly IDbConnectionFactory _factory;

    public FinanceBudgetRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record BudgetRow(
        Guid Id, Guid OrganizationId, Guid CategoryId,
        int Year, int Month, decimal Amount,
        Guid CreatedBy, DateTime CreatedAt, DateTime UpdatedAt
    );

    private record BudgetWithCategoryRow(
        Guid Id, Guid CategoryId, string CategoryName, string? CategoryIcon,
        int Year, int Month, decimal Amount
    );

    public async Task<IReadOnlyList<BudgetWithCategoryItem>> GetByOrgMonthAsync(Guid orgId, int year, int month, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
              fb.id, fb.category_id, fc.name AS category_name, fc.icon AS category_icon,
              fb.year, fb.month, fb.amount
            FROM public.finance_budgets fb
            JOIN public.finance_categories fc ON fc.id = fb.category_id
            WHERE fb.organization_id = @OrgId
              AND fb.year = @Year
              AND fb.month = @Month
            ORDER BY fc.sort_order ASC, fc.name ASC
            """;

        var rows = await conn.QueryAsync<BudgetWithCategoryRow>(sql, new { OrgId = orgId, Year = year, Month = month });
        return rows.Select(r => new BudgetWithCategoryItem(
            r.Id, r.CategoryId, r.CategoryName, r.CategoryIcon,
            r.Year, r.Month, r.Amount
        )).ToList();
    }

    public async Task<IReadOnlyList<BudgetWithCategoryItem>> GetByOrgYearAsync(Guid orgId, int year, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
              fb.id, fb.category_id, fc.name AS category_name, fc.icon AS category_icon,
              fb.year, fb.month, fb.amount
            FROM public.finance_budgets fb
            JOIN public.finance_categories fc ON fc.id = fb.category_id
            WHERE fb.organization_id = @OrgId
              AND fb.year = @Year
            ORDER BY fb.month ASC, fc.sort_order ASC, fc.name ASC
            """;

        var rows = await conn.QueryAsync<BudgetWithCategoryRow>(sql, new { OrgId = orgId, Year = year });
        return rows.Select(r => new BudgetWithCategoryItem(
            r.Id, r.CategoryId, r.CategoryName, r.CategoryIcon,
            r.Year, r.Month, r.Amount
        )).ToList();
    }

    public async Task<FinanceBudget> UpsertAsync(FinanceBudget budget, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.finance_budgets
                (id, organization_id, category_id, year, month, amount, created_by, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @CategoryId, @Year, @Month, @Amount, @CreatedBy, @CreatedAt, @UpdatedAt)
            ON CONFLICT (organization_id, category_id, year, month) DO UPDATE SET
                amount = EXCLUDED.amount,
                updated_at = now()
            RETURNING id, organization_id, category_id, year, month, amount, created_by, created_at, updated_at
            """;

        var row = await conn.QuerySingleAsync<BudgetRow>(sql, new
        {
            budget.Id,
            budget.OrganizationId,
            budget.CategoryId,
            budget.Year,
            budget.Month,
            budget.Amount,
            budget.CreatedBy,
            CreatedAt = budget.CreatedAt.UtcDateTime,
            UpdatedAt = budget.UpdatedAt.UtcDateTime
        });

        return MapRow(row);
    }

    public async Task<int> CopyMonthAsync(Guid orgId, int fromYear, int fromMonth, int toYear, int toMonth, Guid createdBy, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.finance_budgets
                (id, organization_id, category_id, year, month, amount, created_by, created_at, updated_at)
            SELECT
                gen_random_uuid(), organization_id, category_id, @ToYear, @ToMonth, amount, @CreatedBy, now(), now()
            FROM public.finance_budgets
            WHERE organization_id = @OrgId AND year = @FromYear AND month = @FromMonth
            ON CONFLICT (organization_id, category_id, year, month) DO UPDATE SET
                amount = EXCLUDED.amount,
                updated_at = now()
            """;

        return await conn.ExecuteAsync(sql, new
        {
            OrgId = orgId,
            FromYear = fromYear,
            FromMonth = fromMonth,
            ToYear = toYear,
            ToMonth = toMonth,
            CreatedBy = createdBy
        });
    }

    private static FinanceBudget MapRow(BudgetRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        CategoryId = row.CategoryId,
        Year = row.Year,
        Month = row.Month,
        Amount = row.Amount,
        CreatedBy = row.CreatedBy,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
