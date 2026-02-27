using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Infrastructure.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class BlockRepository : IBlockRepository
{
    private readonly IDbConnectionFactory _factory;

    public BlockRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    // Liste sorgusu için (unit_count dahil, organization_id hariç)
    private record BlockListRow(
        Guid Id, string Name, string BlockType, bool IsDefault,
        DateTime CreatedAt, DateTime UpdatedAt, long UnitCount
    );

    // Tekil sorgu / RETURNING için (organization_id dahil, unit_count hariç)
    private record BlockDetailRow(
        Guid Id, Guid OrganizationId, string Name, string BlockType, bool IsDefault,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    public async Task<IReadOnlyList<BlockListItem>> GetByOrgIdAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                b.id, b.name, b.block_type, b.is_default, b.created_at, b.updated_at,
                COUNT(u.id) FILTER (WHERE u.deleted_at IS NULL) AS unit_count
            FROM public.blocks b
            LEFT JOIN public.units u ON u.block_id = b.id
            WHERE b.organization_id = @OrgId AND b.deleted_at IS NULL
            GROUP BY b.id
            ORDER BY b.is_default DESC, b.name ASC
            """;

        var rows = await conn.QueryAsync<BlockListRow>(sql, new { OrgId = orgId });
        return rows
            .Select(r => new BlockListItem(
                r.Id, r.Name, r.BlockType, r.IsDefault,
                (int)r.UnitCount,
                new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)))
            .ToList();
    }

    public async Task<Block?> GetByIdAsync(Guid blockId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, name, block_type, is_default, created_at, updated_at
            FROM public.blocks
            WHERE id = @BlockId AND deleted_at IS NULL
            """;

        var row = await conn.QuerySingleOrDefaultAsync<BlockDetailRow>(sql, new { BlockId = blockId });
        if (row is null) return null;

        return new Block
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            Name = row.Name,
            BlockType = row.BlockType,
            IsDefault = row.IsDefault,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    public async Task<bool> ExistsByNameAsync(Guid orgId, string name, Guid? excludeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.blocks
                WHERE organization_id = @OrgId
                  AND lower(trim(name)) = lower(trim(@Name))
                  AND deleted_at IS NULL
                  AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { OrgId = orgId, Name = name, ExcludeId = excludeId });
    }

    public async Task<int> GetActiveUnitCountAsync(Guid blockId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT COUNT(*) FROM public.units
            WHERE block_id = @BlockId AND deleted_at IS NULL
            """;
        var count = await conn.QuerySingleAsync<long>(sql, new { BlockId = blockId });
        return (int)count;
    }

    public async Task<Block> CreateAsync(Block block, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.blocks (id, organization_id, name, block_type, is_default, created_at, updated_at)
            VALUES (@Id, @OrganizationId, @Name, @BlockType, @IsDefault, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, name, block_type, is_default, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<BlockDetailRow>(sql, new
        {
            block.Id,
            block.OrganizationId,
            block.Name,
            block.BlockType,
            block.IsDefault,
            CreatedAt = block.CreatedAt.UtcDateTime,
            UpdatedAt = block.UpdatedAt.UtcDateTime
        });
        return new Block
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            Name = row.Name,
            BlockType = row.BlockType,
            IsDefault = row.IsDefault,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    public async Task UpdateAsync(Block block, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.blocks
            SET name = @Name, updated_at = @UpdatedAt
            WHERE id = @Id AND deleted_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new
        {
            block.Id,
            block.Name,
            UpdatedAt = block.UpdatedAt.UtcDateTime
        });
    }

    public async Task SoftDeleteAsync(Guid blockId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.blocks
            SET deleted_at = NOW(), updated_at = NOW()
            WHERE id = @BlockId AND deleted_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new { BlockId = blockId });
    }
}
