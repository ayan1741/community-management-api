using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class DueTypeRepository : IDueTypeRepository
{
    private readonly IDbConnectionFactory _factory;

    public DueTypeRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record DueTypeRow(
        Guid Id, Guid OrganizationId, string Name, string? Description,
        decimal DefaultAmount, string? CategoryAmounts, bool IsActive,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    public async Task<IReadOnlyList<DueType>> GetByOrgIdAsync(Guid orgId, bool? isActive, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        var activeFilter = isActive.HasValue ? "AND is_active = @IsActive" : "";

        var sql = $"""
            SELECT id, organization_id, name, description, default_amount, category_amounts, is_active, created_at, updated_at
            FROM public.due_types
            WHERE organization_id = @OrgId
              {activeFilter}
            ORDER BY name ASC
            """;

        var rows = await conn.QueryAsync<DueTypeRow>(sql, new { OrgId = orgId, IsActive = isActive });
        return rows.Select(MapRow).ToList();
    }

    public async Task<DueType?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, name, description, default_amount, category_amounts, is_active, created_at, updated_at
            FROM public.due_types
            WHERE id = @Id
            """;
        var row = await conn.QuerySingleOrDefaultAsync<DueTypeRow>(sql, new { Id = id });
        return row is null ? null : MapRow(row);
    }

    public async Task<bool> ExistsByNameAsync(Guid orgId, string name, Guid? excludeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.due_types
                WHERE organization_id = @OrgId
                  AND lower(trim(name)) = lower(trim(@Name))
                  AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { OrgId = orgId, Name = name, ExcludeId = excludeId });
    }

    public async Task<bool> HasAccrualsAsync(Guid dueTypeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.unit_dues
                WHERE due_type_id = @DueTypeId AND status != 'cancelled'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { DueTypeId = dueTypeId });
    }

    public async Task<DueType> CreateAsync(DueType dueType, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.due_types
                (id, organization_id, name, description, default_amount, category_amounts, is_active, created_at, updated_at)
            VALUES
                (@Id, @OrganizationId, @Name, @Description, @DefaultAmount, @CategoryAmounts::jsonb, @IsActive, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, name, description, default_amount, category_amounts, is_active, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<DueTypeRow>(sql, new
        {
            dueType.Id,
            dueType.OrganizationId,
            dueType.Name,
            dueType.Description,
            dueType.DefaultAmount,
            dueType.CategoryAmounts,
            dueType.IsActive,
            CreatedAt = dueType.CreatedAt.UtcDateTime,
            UpdatedAt = dueType.UpdatedAt.UtcDateTime
        });
        return MapRow(row);
    }

    public async Task UpdateAsync(DueType dueType, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.due_types
            SET name = @Name, description = @Description, default_amount = @DefaultAmount,
                category_amounts = @CategoryAmounts::jsonb, is_active = @IsActive, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, new
        {
            dueType.Id,
            dueType.Name,
            dueType.Description,
            dueType.DefaultAmount,
            dueType.CategoryAmounts,
            dueType.IsActive,
            UpdatedAt = dueType.UpdatedAt.UtcDateTime
        });
    }

    private static DueType MapRow(DueTypeRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        Name = row.Name,
        Description = row.Description,
        DefaultAmount = row.DefaultAmount,
        CategoryAmounts = row.CategoryAmounts,
        IsActive = row.IsActive,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
