using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class FinanceCategoryRepository : IFinanceCategoryRepository
{
    private readonly IDbConnectionFactory _factory;

    public FinanceCategoryRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record CategoryRow(
        Guid Id, Guid OrganizationId, string Name, string Type,
        Guid? ParentId, string? Icon, bool IsSystem, bool IsActive,
        int SortOrder, DateTime CreatedAt, DateTime UpdatedAt
    );

    public async Task<IReadOnlyList<FinanceCategory>> GetByOrgIdAsync(Guid orgId, string? type, bool? isActive, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var typeFilter = type is not null ? "AND type = @Type" : "";
        var activeFilter = isActive.HasValue ? "AND is_active = @IsActive" : "";

        var sql = $"""
            SELECT id, organization_id, name, type, parent_id, icon, is_system, is_active, sort_order, created_at, updated_at
            FROM public.finance_categories
            WHERE organization_id = @OrgId
              {typeFilter}
              {activeFilter}
            ORDER BY sort_order ASC, name ASC
            """;

        var rows = await conn.QueryAsync<CategoryRow>(sql, new { OrgId = orgId, Type = type, IsActive = isActive });
        return rows.Select(MapRow).ToList();
    }

    public async Task<FinanceCategory?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, name, type, parent_id, icon, is_system, is_active, sort_order, created_at, updated_at
            FROM public.finance_categories
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<CategoryRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<bool> ExistsByNameAsync(Guid orgId, string type, string name, Guid? excludeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.finance_categories
                WHERE organization_id = @OrgId
                  AND type = @Type
                  AND lower(trim(name)) = lower(trim(@Name))
                  AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { OrgId = orgId, Type = type, Name = name, ExcludeId = excludeId });
    }

    public async Task<bool> HasRecordsAsync(Guid categoryId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.finance_records
                WHERE category_id = @CategoryId AND deleted_at IS NULL
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { CategoryId = categoryId });
    }

    public async Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.finance_categories
                WHERE parent_id = @CategoryId
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { CategoryId = categoryId });
    }

    public async Task<bool> HasCategoriesAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.finance_categories
                WHERE organization_id = @OrgId
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { OrgId = orgId });
    }

    public async Task<FinanceCategory> CreateAsync(FinanceCategory category, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.finance_categories
                (id, organization_id, name, type, parent_id, icon, is_system, is_active, sort_order, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @Name, @Type, @ParentId, @Icon, @IsSystem, @IsActive, @SortOrder, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, name, type, parent_id, icon, is_system, is_active, sort_order, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<CategoryRow>(sql, new
        {
            category.Id,
            category.OrganizationId,
            category.Name,
            category.Type,
            category.ParentId,
            category.Icon,
            category.IsSystem,
            category.IsActive,
            category.SortOrder,
            CreatedAt = category.CreatedAt.UtcDateTime,
            UpdatedAt = category.UpdatedAt.UtcDateTime
        });
        return MapRow(row);
    }

    public async Task CreateBulkAsync(IReadOnlyList<FinanceCategory> categories, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.finance_categories
                (id, organization_id, name, type, parent_id, icon, is_system, is_active, sort_order, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @Name, @Type, @ParentId, @Icon, @IsSystem, @IsActive, @SortOrder, @CreatedAt, @UpdatedAt)
            ON CONFLICT (organization_id, type, lower(trim(name))) DO NOTHING
            """;

        foreach (var cat in categories)
        {
            await conn.ExecuteAsync(sql, new
            {
                cat.Id,
                cat.OrganizationId,
                cat.Name,
                cat.Type,
                cat.ParentId,
                cat.Icon,
                cat.IsSystem,
                cat.IsActive,
                cat.SortOrder,
                CreatedAt = cat.CreatedAt.UtcDateTime,
                UpdatedAt = cat.UpdatedAt.UtcDateTime
            });
        }
    }

    public async Task UpdateAsync(FinanceCategory category, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.finance_categories
            SET name = @Name, icon = @Icon, is_active = @IsActive, sort_order = @SortOrder, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, new
        {
            category.Id,
            category.Name,
            category.Icon,
            category.IsActive,
            category.SortOrder,
            UpdatedAt = category.UpdatedAt.UtcDateTime
        });
    }

    public async Task DeactivateChildrenAsync(Guid parentId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.finance_categories
            SET is_active = false, updated_at = now()
            WHERE parent_id = @ParentId AND is_active = true
            """;
        await conn.ExecuteAsync(sql, new { ParentId = parentId });
    }

    private static FinanceCategory MapRow(CategoryRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        Name = row.Name,
        Type = row.Type,
        ParentId = row.ParentId,
        Icon = row.Icon,
        IsSystem = row.IsSystem,
        IsActive = row.IsActive,
        SortOrder = row.SortOrder,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
