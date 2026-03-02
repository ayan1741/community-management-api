using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class UnitResidentRepository : IUnitResidentRepository
{
    private readonly IDbConnectionFactory _factory;

    public UnitResidentRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    private record UnitResidentRow(
        Guid Id, Guid UnitId, Guid UserId, Guid OrganizationId,
        string ResidentType, bool IsPrimary, string Status,
        DateTime? RemovedAt, Guid? RemovedBy,
        DateTime CreatedAt, DateTime UpdatedAt
    );

    private record UnitResidentListRow(
        Guid Id, Guid UserId, string FullName, string? Phone,
        string ResidentType, bool IsPrimary, DateTime CreatedAt
    );

    public async Task<IReadOnlyList<UnitResidentListItem>> GetByUnitIdAsync(Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                ur.id, ur.user_id, p.full_name, p.phone,
                ur.resident_type, ur.is_primary, ur.created_at
            FROM public.unit_residents ur
            JOIN public.profiles p ON p.id = ur.user_id
            WHERE ur.unit_id = @UnitId
              AND ur.status = 'active'
            ORDER BY ur.is_primary DESC, ur.created_at
            """;

        var rows = await conn.QueryAsync<UnitResidentListRow>(sql, new { UnitId = unitId });
        return rows
            .Select(r => new UnitResidentListItem(
                r.Id, r.UserId, r.FullName, r.Phone,
                r.ResidentType, r.IsPrimary,
                new DateTimeOffset(r.CreatedAt, TimeSpan.Zero)))
            .ToList();
    }

    public async Task<UnitResident?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, unit_id, user_id, organization_id, resident_type, is_primary,
                   status, removed_at, removed_by, created_at, updated_at
            FROM public.unit_residents
            WHERE id = @Id AND status = 'active'
            """;

        var row = await conn.QuerySingleOrDefaultAsync<UnitResidentRow>(sql, new { Id = id });
        return row is null ? null : MapEntity(row);
    }

    public async Task<bool> ExistsActiveAsync(Guid unitId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS(
                SELECT 1 FROM public.unit_residents
                WHERE unit_id = @UnitId AND user_id = @UserId AND status = 'active'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UnitId = unitId, UserId = userId });
    }

    public async Task<UnitResident?> CreateAsync(UnitResident resident, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.unit_residents
                (id, unit_id, user_id, organization_id, resident_type, is_primary, status)
            VALUES
                (@Id, @UnitId, @UserId, @OrganizationId, @ResidentType, @IsPrimary, 'active')
            ON CONFLICT (unit_id, user_id) WHERE status = 'active' DO NOTHING
            RETURNING id, unit_id, user_id, organization_id, resident_type,
                      is_primary, status, removed_at, removed_by, created_at, updated_at
            """;

        var row = await conn.QuerySingleOrDefaultAsync<UnitResidentRow>(sql, new
        {
            resident.Id,
            resident.UnitId,
            resident.UserId,
            resident.OrganizationId,
            ResidentType = resident.ResidentType.ToString().ToLower(),
            resident.IsPrimary
        });

        return row is null ? null : MapEntity(row);
    }

    public async Task RemoveAsync(Guid id, Guid removedBy, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.unit_residents
            SET status = 'removed', removed_at = now(), removed_by = @RemovedBy, updated_at = now()
            WHERE id = @Id AND status = 'active'
            """;
        await conn.ExecuteAsync(sql, new { Id = id, RemovedBy = removedBy });
    }

    private static UnitResident MapEntity(UnitResidentRow r) => new()
    {
        Id = r.Id,
        UnitId = r.UnitId,
        UserId = r.UserId,
        OrganizationId = r.OrganizationId,
        ResidentType = Enum.Parse<Core.Enums.ResidentType>(r.ResidentType, ignoreCase: true),
        IsPrimary = r.IsPrimary,
        Status = Enum.Parse<Core.Enums.UnitResidentStatus>(r.Status, ignoreCase: true),
        RemovedAt = r.RemovedAt.HasValue ? new DateTimeOffset(r.RemovedAt.Value, TimeSpan.Zero) : null,
        RemovedBy = r.RemovedBy,
        CreatedAt = new DateTimeOffset(r.CreatedAt, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(r.UpdatedAt, TimeSpan.Zero)
    };
}
