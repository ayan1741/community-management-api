using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class OrganizationRepository : IOrganizationRepository
{
    private readonly IDbConnectionFactory _factory;

    public OrganizationRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<Organization> CreateAsync(Organization organization, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.organizations (id, name, org_type, status, created_by, address_district, address_city, contact_phone, created_at, updated_at)
            VALUES (@Id, @Name, @OrgType, @Status, @CreatedBy, @AddressDistrict, @AddressCity, @ContactPhone, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<Organization>(sql, organization);
    }

    public async Task<Organization?> GetByIdAsync(Guid orgId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, name, org_type, status, address_district, address_city, contact_phone, created_at, updated_at
            FROM public.organizations
            WHERE id = @OrgId
            """;
        return await conn.QuerySingleOrDefaultAsync<Organization>(sql, new { OrgId = orgId });
    }

    public async Task<bool> ExistsForUserAsync(Guid userId, string name, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.organizations o
                JOIN public.organization_members om ON om.organization_id = o.id
                WHERE om.user_id = @UserId AND o.name = @Name AND om.status = 'active'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UserId = userId, Name = name });
    }
}
