using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Infrastructure.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class ProfileRepository : IProfileRepository
{
    private readonly IDbConnectionFactory _factory;

    public ProfileRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<UserProfile?> GetByIdAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, full_name, phone, avatar_url, kvkk_consent_at, deleted_at, created_at, updated_at
            FROM public.profiles
            WHERE id = @UserId AND deleted_at IS NULL
            """;
        return await conn.QuerySingleOrDefaultAsync<UserProfile>(sql, new { UserId = userId });
    }

    public async Task UpdateAsync(UserProfile profile, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            UPDATE public.profiles
            SET full_name = @FullName, phone = @Phone, updated_at = @UpdatedAt
            WHERE id = @Id
            """;
        await conn.ExecuteAsync(sql, new
        {
            profile.Id,
            profile.FullName,
            profile.Phone,
            profile.UpdatedAt
        });
    }

    public async Task RecordKvkkConsentAsync(Guid userId, DateTimeOffset consentAt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            UPDATE public.profiles
            SET kvkk_consent_at = @ConsentAt, updated_at = @ConsentAt
            WHERE id = @UserId
            """;
        await conn.ExecuteAsync(sql, new { UserId = userId, ConsentAt = consentAt });
    }

    public async Task MarkDeletionRequestedAsync(Guid userId, DateTimeOffset requestedAt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.profiles
            SET deletion_requested_at = @RequestedAt, updated_at = @RequestedAt
            WHERE id = @UserId
            """;
        await conn.ExecuteAsync(sql, new { UserId = userId, RequestedAt = requestedAt });
    }

    public async Task<IReadOnlyList<Guid>> GetDeletionRequestedExpiredAsync(DateTimeOffset before, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            SELECT id FROM public.profiles
            WHERE deletion_requested_at IS NOT NULL
              AND deletion_requested_at <= @Before
              AND deleted_at IS NULL
            """;
        return (await conn.QueryAsync<Guid>(sql, new { Before = before })).ToList();
    }

    public async Task SoftDeleteAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.profiles
            SET deleted_at = now(), updated_at = now()
            WHERE id = @UserId
            """;
        await conn.ExecuteAsync(sql, new { UserId = userId });
    }

    public async Task<MyContextResult> GetFullContextAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        const string profileSql = """
            SELECT id, full_name, phone, avatar_url, kvkk_consent_at, deleted_at, created_at, updated_at
            FROM public.profiles
            WHERE id = @UserId AND deleted_at IS NULL
            """;

        const string membershipSql = """
            SELECT
                om.organization_id,
                o.name AS organization_name,
                om.role,
                om.status,
                u.id AS unit_id,
                u.unit_number,
                b.name AS block_name,
                ur.resident_type
            FROM public.organization_members om
            JOIN public.organizations o ON o.id = om.organization_id
            LEFT JOIN public.unit_residents ur ON ur.user_id = om.user_id
                AND ur.organization_id = om.organization_id
                AND ur.status = 'active'
            LEFT JOIN public.units u ON u.id = ur.unit_id
            LEFT JOIN public.blocks b ON b.id = u.block_id
            WHERE om.user_id = @UserId
              AND om.status IN ('active', 'suspended')
            ORDER BY om.organization_id, u.unit_number
            """;

        var profile = await conn.QuerySingleOrDefaultAsync<UserProfile>(profileSql, new { UserId = userId });
        if (profile is null)
            return new MyContextResult(new UserProfile { Id = userId }, Array.Empty<MembershipContext>());

        var rows = (await conn.QueryAsync<MembershipRow>(membershipSql, new { UserId = userId })).ToList();

        var memberships = rows
            .GroupBy(r => r.OrganizationId)
            .Select(g =>
            {
                var first = g.First();
                var units = g
                    .Where(r => r.UnitId.HasValue)
                    .Select(r => new UnitContext(
                        r.UnitId!.Value,
                        r.UnitNumber!,
                        r.BlockName ?? "",
                        r.ResidentType ?? "unspecified"))
                    .ToList();

                return new MembershipContext(
                    g.Key,
                    first.OrganizationName,
                    first.Role,
                    first.Status,
                    units);
            })
            .ToList();

        return new MyContextResult(profile, memberships);
    }

    private record MembershipRow(
        Guid OrganizationId,
        string OrganizationName,
        string Role,
        string Status,
        Guid? UnitId,
        string? UnitNumber,
        string? BlockName,
        string? ResidentType
    );
}
