using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class UnitRepository : IUnitRepository
{
    private readonly IDbConnectionFactory _factory;

    public UnitRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record UnitListRow(
        Guid Id, Guid BlockId, string BlockName, string UnitNumber, string UnitType,
        int? Floor, decimal? AreaSqm, bool IsOccupied,
        DateTime CreatedAt, long TotalCount
    );

    private record UnitDetailRow(
        Guid Id, Guid OrganizationId, Guid BlockId, string UnitNumber, string UnitType,
        int? Floor, decimal? AreaSqm, string? Notes, DateTime CreatedAt, DateTime UpdatedAt
    );

    private record UnitDropdownRow(
        Guid Id, Guid BlockId, string BlockName, string UnitNumber, bool IsOccupied
    );

    public async Task<(IReadOnlyList<UnitListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, Guid? blockId, string? unitType, bool? isOccupied,
        string? search, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        var blockFilter = blockId.HasValue ? "AND u.block_id = @BlockId" : "";
        var typeFilter = unitType is not null ? "AND u.unit_type = @UnitType" : "";
        var searchFilter = search is not null ? "AND u.unit_number ILIKE '%' || @Search || '%'" : "";

        var occupiedFilter = isOccupied switch
        {
            true => "AND EXISTS(SELECT 1 FROM public.unit_residents ur WHERE ur.unit_id = u.id AND ur.status = 'active')",
            false => "AND NOT EXISTS(SELECT 1 FROM public.unit_residents ur WHERE ur.unit_id = u.id AND ur.status = 'active')",
            null => ""
        };

        var sql = $"""
            SELECT
                u.id, u.block_id, b.name AS block_name, u.unit_number, u.unit_type,
                u.floor, u.area_sqm,
                EXISTS(
                    SELECT 1 FROM public.unit_residents ur
                    WHERE ur.unit_id = u.id AND ur.status = 'active'
                ) AS is_occupied,
                u.created_at,
                COUNT(*) OVER() AS total_count
            FROM public.units u
            JOIN public.blocks b ON b.id = u.block_id
            WHERE u.organization_id = @OrgId
              AND u.deleted_at IS NULL
              {blockFilter}
              {typeFilter}
              {searchFilter}
              {occupiedFilter}
            ORDER BY b.name ASC, u.floor ASC NULLS LAST, u.unit_number ASC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<UnitListRow>(sql, new
        {
            OrgId = orgId,
            BlockId = blockId,
            UnitType = unitType,
            Search = search,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        if (rows.Count == 0)
            return (Array.Empty<UnitListItem>(), 0);

        var totalCount = (int)rows[0].TotalCount;
        var items = rows
            .Select(r => new UnitListItem(
                r.Id, r.BlockId, r.BlockName, r.UnitNumber, r.UnitType,
                r.Floor, r.AreaSqm, r.IsOccupied,
                new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)))
            .ToList();

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<UnitDropdownItem>> GetDropdownByOrgIdAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                u.id, u.block_id, b.name AS block_name, u.unit_number,
                EXISTS(
                    SELECT 1 FROM public.unit_residents ur
                    WHERE ur.unit_id = u.id AND ur.status = 'active'
                ) AS is_occupied
            FROM public.units u
            JOIN public.blocks b ON b.id = u.block_id
            WHERE u.organization_id = @OrgId AND u.deleted_at IS NULL
            ORDER BY b.name ASC, u.unit_number ASC
            """;

        var rows = await conn.QueryAsync<UnitDropdownRow>(sql, new { OrgId = orgId });
        return rows
            .Select(r => new UnitDropdownItem(r.Id, r.BlockId, r.BlockName, r.UnitNumber, r.IsOccupied))
            .ToList();
    }

    public async Task<Unit?> GetByIdAsync(Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, block_id, unit_number, unit_type, floor, area_sqm, notes, created_at, updated_at
            FROM public.units
            WHERE id = @UnitId AND deleted_at IS NULL
            """;

        var row = await conn.QuerySingleOrDefaultAsync<UnitDetailRow>(sql, new { UnitId = unitId });
        if (row is null) return null;

        return new Unit
        {
            Id = row.Id,
            OrganizationId = row.OrganizationId,
            BlockId = row.BlockId,
            UnitNumber = row.UnitNumber,
            UnitType = row.UnitType,
            Floor = row.Floor,
            AreaSqm = row.AreaSqm,
            Notes = row.Notes,
            CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
            UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
        };
    }

    public async Task<bool> ExistsByNumberAsync(Guid blockId, string unitNumber, Guid? excludeId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.units
                WHERE block_id = @BlockId
                  AND lower(trim(unit_number)) = lower(trim(@UnitNumber))
                  AND deleted_at IS NULL
                  AND (@ExcludeId IS NULL OR id <> @ExcludeId)
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { BlockId = blockId, UnitNumber = unitNumber, ExcludeId = excludeId });
    }

    public async Task<IReadOnlyList<string>> GetExistingNumbersAsync(
        Guid blockId, IReadOnlyList<string> numbers, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT unit_number FROM public.units
            WHERE block_id = @BlockId
              AND deleted_at IS NULL
              AND lower(trim(unit_number)) = ANY(@Numbers)
            """;
        var result = await conn.QueryAsync<string>(sql, new
        {
            BlockId = blockId,
            Numbers = numbers.Select(n => n.ToLower().Trim()).ToArray()
        });
        return result.ToList();
    }

    public async Task<bool> HasActiveResidentAsync(Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.unit_residents
                WHERE unit_id = @UnitId AND status = 'active'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UnitId = unitId });
    }

    public async Task<Unit> CreateAsync(Unit unit, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.units (id, organization_id, block_id, unit_number, unit_type, floor, area_sqm, notes, created_at, updated_at)
            VALUES (@Id, @OrganizationId, @BlockId, @UnitNumber, @UnitType, @Floor, @AreaSqm, @Notes, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, block_id, unit_number, unit_type, floor, area_sqm, notes, created_at, updated_at
            """;
        var row = await conn.QuerySingleAsync<UnitDetailRow>(sql, new
        {
            unit.Id,
            unit.OrganizationId,
            unit.BlockId,
            unit.UnitNumber,
            unit.UnitType,
            unit.Floor,
            unit.AreaSqm,
            unit.Notes,
            CreatedAt = unit.CreatedAt.UtcDateTime,
            UpdatedAt = unit.UpdatedAt.UtcDateTime
        });
        return MapDetailRow(row);
    }

    public async Task<IReadOnlyList<Unit>> CreateBulkAsync(IReadOnlyList<Unit> units, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.units (id, organization_id, block_id, unit_number, unit_type, floor, area_sqm, notes, created_at, updated_at)
            VALUES (@Id, @OrganizationId, @BlockId, @UnitNumber, @UnitType, @Floor, @AreaSqm, @Notes, @CreatedAt, @UpdatedAt)
            RETURNING id, organization_id, block_id, unit_number, unit_type, floor, area_sqm, notes, created_at, updated_at
            """;

        var result = new List<Unit>();
        foreach (var unit in units)
        {
            var row = await conn.QuerySingleAsync<UnitDetailRow>(sql, new
            {
                unit.Id,
                unit.OrganizationId,
                unit.BlockId,
                unit.UnitNumber,
                unit.UnitType,
                unit.Floor,
                unit.AreaSqm,
                unit.Notes,
                CreatedAt = unit.CreatedAt.UtcDateTime,
                UpdatedAt = unit.UpdatedAt.UtcDateTime
            });
            result.Add(MapDetailRow(row));
        }
        return result;
    }

    public async Task UpdateAsync(Unit unit, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.units
            SET unit_number = @UnitNumber, unit_type = @UnitType, floor = @Floor,
                area_sqm = @AreaSqm, notes = @Notes, updated_at = @UpdatedAt
            WHERE id = @Id AND deleted_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new
        {
            unit.Id,
            unit.UnitNumber,
            unit.UnitType,
            unit.Floor,
            unit.AreaSqm,
            unit.Notes,
            UpdatedAt = unit.UpdatedAt.UtcDateTime
        });
    }

    public async Task SoftDeleteAsync(Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.units
            SET deleted_at = NOW(), updated_at = NOW()
            WHERE id = @UnitId AND deleted_at IS NULL
            """;
        await conn.ExecuteAsync(sql, new { UnitId = unitId });
    }

    private static Unit MapDetailRow(UnitDetailRow row) => new()
    {
        Id = row.Id,
        OrganizationId = row.OrganizationId,
        BlockId = row.BlockId,
        UnitNumber = row.UnitNumber,
        UnitType = row.UnitType,
        Floor = row.Floor,
        AreaSqm = row.AreaSqm,
        Notes = row.Notes,
        CreatedAt = new DateTimeOffset(row.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(row.UpdatedAt, TimeSpan.Zero)
    };
}
