using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Infrastructure.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class MemberRepository : IMemberRepository
{
    private readonly IDbConnectionFactory _factory;

    public MemberRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<OrganizationMember?> GetByUserIdAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, user_id, role, status, invited_by, suspended_at, suspended_by, created_at, updated_at
            FROM public.organization_members
            WHERE organization_id = @OrgId AND user_id = @UserId
            """;
        return await conn.QuerySingleOrDefaultAsync<OrganizationMember>(sql, new { OrgId = orgId, UserId = userId });
    }

    public async Task<(IReadOnlyList<MemberListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, MemberStatus? status, MemberRole? role, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        var statusFilter = status.HasValue ? "AND om.status = @Status" : "";
        var roleFilter = role.HasValue ? "AND om.role = @Role" : "";

        var sql = $"""
            SELECT
                p.id AS user_id,
                p.full_name,
                p.phone,
                p.avatar_url,
                om.role,
                om.status,
                u.unit_number,
                b.name AS block_name,
                om.created_at AS joined_at,
                COUNT(*) OVER() AS total_count
            FROM public.organization_members om
            JOIN public.profiles p ON p.id = om.user_id
            LEFT JOIN public.unit_residents ur ON ur.user_id = om.user_id
                AND ur.organization_id = om.organization_id AND ur.status = 'active'
            LEFT JOIN public.units u ON u.id = ur.unit_id
            LEFT JOIN public.blocks b ON b.id = u.block_id
            WHERE om.organization_id = @OrgId
              {statusFilter}
              {roleFilter}
            ORDER BY om.created_at ASC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<MemberRow>(sql, new
        {
            OrgId = orgId,
            Status = status?.ToString().ToLower(),
            Role = role?.ToString().ToLower(),
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows
            .GroupBy(r => r.UserId)
            .Select(g =>
            {
                var first = g.First();
                var units = g
                    .Where(r => r.UnitNumber is not null)
                    .Select(r => new MemberUnitInfo(r.UnitNumber!, r.BlockName ?? ""))
                    .ToList();
                return new MemberListItem(
                    first.UserId, first.FullName, first.Phone, first.AvatarUrl,
                    first.Role, first.Status, units, new DateTimeOffset(first.JoinedAt, TimeSpan.Zero));
            })
            .ToList();

        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<int> GetAdminCountAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT COUNT(*) FROM public.organization_members
            WHERE organization_id = @OrgId AND role = 'admin' AND status = 'active'
            """;
        return await conn.QuerySingleAsync<int>(sql, new { OrgId = orgId });
    }

    public async Task<bool> IsLastAdminAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        // If orgId is empty, check across all organizations
        if (orgId == Guid.Empty)
        {
            const string sql = """
                SELECT EXISTS (
                    SELECT 1 FROM public.organization_members om
                    WHERE om.user_id = @UserId AND om.role = 'admin' AND om.status = 'active'
                    AND NOT EXISTS (
                        SELECT 1 FROM public.organization_members om2
                        WHERE om2.organization_id = om.organization_id
                          AND om2.role = 'admin' AND om2.status = 'active'
                          AND om2.user_id != @UserId
                    )
                )
                """;
            return await conn.QuerySingleAsync<bool>(sql, new { UserId = userId });
        }

        const string singleOrgSql = """
            SELECT COUNT(*) FROM public.organization_members
            WHERE organization_id = @OrgId AND role = 'admin' AND status = 'active' AND user_id != @UserId
            """;
        var otherAdminCount = await conn.QuerySingleAsync<int>(singleOrgSql, new { OrgId = orgId, UserId = userId });
        return otherAdminCount == 0;
    }

    public async Task<bool> IsLastAdminInAnyOrgAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.organization_members om
                WHERE om.user_id = @UserId AND om.role = 'admin' AND om.status = 'active'
                AND NOT EXISTS (
                    SELECT 1 FROM public.organization_members om2
                    WHERE om2.organization_id = om.organization_id
                      AND om2.role = 'admin' AND om2.status = 'active'
                      AND om2.user_id != @UserId
                )
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UserId = userId });
    }

    public async Task UpdateRoleAsync(Guid orgId, Guid userId, MemberRole role, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.organization_members
            SET role = @Role, updated_at = now()
            WHERE organization_id = @OrgId AND user_id = @UserId
            """;
        await conn.ExecuteAsync(sql, new { OrgId = orgId, UserId = userId, Role = role.ToString().ToLower() });
    }

    public async Task UpdateStatusAsync(
        Guid orgId, Guid userId, MemberStatus status, Guid? suspendedBy, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.organization_members
            SET status = @Status,
                suspended_at = CASE WHEN @Status = 'suspended' THEN now() ELSE NULL END,
                suspended_by = CASE WHEN @Status = 'suspended' THEN @SuspendedBy ELSE NULL END,
                updated_at = now()
            WHERE organization_id = @OrgId AND user_id = @UserId
            """;
        await conn.ExecuteAsync(sql, new
        {
            OrgId = orgId,
            UserId = userId,
            Status = status.ToString().ToLower(),
            SuspendedBy = suspendedBy
        });
    }

    public async Task UpsertAsync(OrganizationMember member, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.organization_members
              (id, organization_id, user_id, role, status, invited_by, created_at, updated_at)
            VALUES
              (@Id, @OrganizationId, @UserId, @Role, @Status, @InvitedBy, @CreatedAt, @UpdatedAt)
            ON CONFLICT (organization_id, user_id) DO UPDATE
            SET role = EXCLUDED.role,
                status = EXCLUDED.status,
                updated_at = now()
            """;
        await conn.ExecuteAsync(sql, new
        {
            member.Id,
            member.OrganizationId,
            member.UserId,
            Role = member.Role.ToString().ToLower(),
            Status = member.Status.ToString().ToLower(),
            member.InvitedBy,
            member.CreatedAt,
            member.UpdatedAt
        });
    }

    public async Task RemoveAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.organization_members
            SET status = 'removed', updated_at = now()
            WHERE organization_id = @OrgId AND user_id = @UserId;

            UPDATE public.unit_residents
            SET status = 'removed', updated_at = now()
            WHERE organization_id = @OrgId AND user_id = @UserId AND status = 'active';
            """;
        await conn.ExecuteAsync(sql, new { OrgId = orgId, UserId = userId });
    }

    public async Task<IReadOnlyList<MemberHistoryItem>> GetHistoryAsync(Guid orgId, Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT
                al.action,
                p.full_name AS actor_name,
                al.old_values::text AS old_value,
                al.new_values::text AS new_value,
                al.created_at
            FROM public.audit_logs al
            LEFT JOIN public.profiles p ON p.id = al.actor_id
            WHERE al.organization_id = @OrgId
              AND al.table_name = 'organization_members'
              AND al.record_id = @UserId
            ORDER BY al.created_at DESC
            """;
        var rows = (await conn.QueryAsync<HistoryRow>(sql, new { OrgId = orgId, UserId = userId })).ToList();
        return rows.Select(r => new MemberHistoryItem(
            r.Action, r.ActorName, r.OldValue, r.NewValue,
            new DateTimeOffset(r.CreatedAt, TimeSpan.Zero))).ToList();
    }

    private record HistoryRow(
        string Action,
        string? ActorName,
        string? OldValue,
        string? NewValue,
        DateTime CreatedAt
    );

    private record MemberRow(
        Guid UserId,
        string FullName,
        string? Phone,
        string? AvatarUrl,
        string Role,
        string Status,
        string? UnitNumber,
        string? BlockName,
        DateTime JoinedAt,
        long TotalCount
    );
}
