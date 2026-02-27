using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Infrastructure.Common;
using Dapper;

namespace CommunityManagement.Infrastructure.Repositories;

public class InvitationRepository : IInvitationRepository
{
    private readonly IDbConnectionFactory _factory;

    public InvitationRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<Invitation> CreateAsync(Invitation invitation, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.invitation_codes (id, organization_id, unit_id, invitation_code, code_status, created_by, expires_at, created_at, updated_at)
            VALUES (@Id, @OrganizationId, @UnitId, @InvitationCode, @CodeStatus, @CreatedBy, @ExpiresAt, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<Invitation>(sql, new
        {
            invitation.Id,
            invitation.OrganizationId,
            invitation.UnitId,
            invitation.InvitationCode,
            CodeStatus = invitation.CodeStatus.ToString().ToLower(),
            invitation.CreatedBy,
            invitation.ExpiresAt,
            invitation.CreatedAt,
            invitation.UpdatedAt
        });
    }

    public async Task<Invitation?> GetByIdAsync(Guid invitationId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, unit_id, invitation_code, code_status, created_by, expires_at, created_at, updated_at
            FROM public.invitation_codes
            WHERE id = @InvitationId
            """;
        return await conn.QuerySingleOrDefaultAsync<Invitation>(sql, new { InvitationId = invitationId });
    }

    public async Task<Invitation?> GetByCodeAsync(string code, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, unit_id, invitation_code, code_status, created_by, expires_at, created_at, updated_at
            FROM public.invitation_codes
            WHERE invitation_code = @Code
            """;
        return await conn.QuerySingleOrDefaultAsync<Invitation>(sql, new { Code = code });
    }

    public async Task<(IReadOnlyList<InvitationListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, CodeStatus? status, Guid? unitId, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        var statusFilter = status.HasValue ? "AND ic.code_status = @Status" : "";
        var unitFilter = unitId.HasValue ? "AND ic.unit_id = @UnitId" : "";

        var sql = $"""
            SELECT
                ic.id AS invitation_id,
                ic.invitation_code,
                u.unit_number,
                b.name AS block_name,
                ic.code_status,
                a.application_status,
                ic.expires_at,
                ic.created_at,
                COUNT(*) OVER() AS total_count
            FROM public.invitation_codes ic
            JOIN public.units u ON u.id = ic.unit_id
            JOIN public.blocks b ON b.id = u.block_id
            LEFT JOIN public.applications a ON a.invitation_id = ic.id
            WHERE ic.organization_id = @OrgId
              {statusFilter}
              {unitFilter}
            ORDER BY ic.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<InvitationRow>(sql, new
        {
            OrgId = orgId,
            Status = status?.ToString().ToLower(),
            UnitId = unitId,
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new InvitationListItem(
            r.InvitationId, r.InvitationCode, r.UnitNumber, r.BlockName,
            r.CodeStatus, r.ApplicationStatus, r.ExpiresAt, r.CreatedAt)).ToList();

        return (items, rows.FirstOrDefault()?.TotalCount ?? 0);
    }

    public async Task UpdateStatusAsync(Guid invitationId, CodeStatus status, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.invitation_codes
            SET code_status = @Status, updated_at = now()
            WHERE id = @InvitationId
            """;
        await conn.ExecuteAsync(sql, new { InvitationId = invitationId, Status = status.ToString().ToLower() });
    }

    public async Task<bool> HasActiveInvitationForUnitAsync(Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.invitation_codes
                WHERE unit_id = @UnitId AND code_status = 'active' AND expires_at > now()
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UnitId = unitId });
    }

    private record InvitationRow(
        Guid InvitationId,
        string InvitationCode,
        string UnitNumber,
        string BlockName,
        string CodeStatus,
        string? ApplicationStatus,
        DateTimeOffset ExpiresAt,
        DateTimeOffset CreatedAt,
        int TotalCount
    );
}
