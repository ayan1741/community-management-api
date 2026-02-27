using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Infrastructure.Common;
using Dapper;
using ApplicationEntity = CommunityManagement.Core.Entities.Application;

namespace CommunityManagement.Infrastructure.Repositories;

public class ApplicationRepository : IApplicationRepository
{
    private readonly IDbConnectionFactory _factory;

    public ApplicationRepository(IDbConnectionFactory factory)
    {
        _factory = factory;
    }

    public async Task<ApplicationEntity> CreateAsync(ApplicationEntity application, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            INSERT INTO public.applications
              (id, organization_id, unit_id, invitation_id, applicant_user_id,
               applicant_resident_type, application_status, rejection_reason,
               reviewed_by, reviewed_at, created_at, updated_at)
            VALUES
              (@Id, @OrganizationId, @UnitId, @InvitationId, @ApplicantUserId,
               @ApplicantResidentType, @ApplicationStatus, @RejectionReason,
               @ReviewedBy, @ReviewedAt, @CreatedAt, @UpdatedAt)
            RETURNING *
            """;
        return await conn.QuerySingleAsync<ApplicationEntity>(sql, new
        {
            application.Id,
            application.OrganizationId,
            application.UnitId,
            application.InvitationId,
            application.ApplicantUserId,
            ApplicantResidentType = application.ApplicantResidentType.ToString().ToLower(),
            ApplicationStatus = application.ApplicationStatus.ToString().ToLower(),
            application.RejectionReason,
            application.ReviewedBy,
            application.ReviewedAt,
            application.CreatedAt,
            application.UpdatedAt
        });
    }

    public async Task<ApplicationEntity?> GetByIdAsync(Guid applicationId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, unit_id, invitation_id, applicant_user_id,
                   applicant_resident_type, application_status, rejection_reason,
                   reviewed_by, reviewed_at, created_at, updated_at
            FROM public.applications
            WHERE id = @ApplicationId
            """;
        return await conn.QuerySingleOrDefaultAsync<ApplicationEntity>(sql, new { ApplicationId = applicationId });
    }

    public async Task<(IReadOnlyList<ApplicationListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, ApplicationStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();

        var statusFilter = status.HasValue ? "AND a.application_status = @Status" : "";

        var sql = $"""
            SELECT
                a.id AS application_id,
                p.full_name AS applicant_name,
                p.phone AS applicant_phone,
                u.unit_number,
                b.name AS block_name,
                a.applicant_resident_type AS resident_type,
                a.created_at AS submitted_at,
                (
                    SELECT COUNT(*) > 1
                    FROM public.applications a2
                    WHERE a2.unit_id = a.unit_id AND a2.application_status = 'pending'
                ) AS duplicate_warning,
                COUNT(*) OVER() AS total_count
            FROM public.applications a
            JOIN public.profiles p ON p.id = a.applicant_user_id
            JOIN public.units u ON u.id = a.unit_id
            JOIN public.blocks b ON b.id = u.block_id
            WHERE a.organization_id = @OrgId
              {statusFilter}
            ORDER BY a.created_at DESC
            LIMIT @PageSize OFFSET @Offset
            """;

        var rows = (await conn.QueryAsync<ApplicationRow>(sql, new
        {
            OrgId = orgId,
            Status = status?.ToString().ToLower(),
            PageSize = pageSize,
            Offset = (page - 1) * pageSize
        })).ToList();

        var items = rows.Select(r => new ApplicationListItem(
            r.ApplicationId, r.ApplicantName, r.ApplicantPhone,
            r.UnitNumber, r.BlockName, r.ResidentType,
            new DateTimeOffset(r.SubmittedAt, TimeSpan.Zero), r.DuplicateWarning)).ToList();

        return (items, (int)(rows.FirstOrDefault()?.TotalCount ?? 0L));
    }

    public async Task<IReadOnlyList<ApplicationEntity>> GetByApplicantAsync(Guid userId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT id, organization_id, unit_id, invitation_id, applicant_user_id,
                   applicant_resident_type, application_status, rejection_reason,
                   reviewed_by, reviewed_at, created_at, updated_at
            FROM public.applications
            WHERE applicant_user_id = @UserId
            ORDER BY created_at DESC
            """;
        return (await conn.QueryAsync<ApplicationEntity>(sql, new { UserId = userId })).ToList();
    }

    public async Task UpdateStatusAsync(
        Guid applicationId, ApplicationStatus status,
        Guid? reviewedBy, string? rejectionReason,
        DateTimeOffset reviewedAt, CancellationToken ct = default)
    {
        using var conn = _factory.CreateServiceRoleConnection();
        const string sql = """
            UPDATE public.applications
            SET application_status = @Status,
                reviewed_by = @ReviewedBy,
                rejection_reason = @RejectionReason,
                reviewed_at = @ReviewedAt,
                updated_at = now()
            WHERE id = @ApplicationId
            """;
        await conn.ExecuteAsync(sql, new
        {
            ApplicationId = applicationId,
            Status = status.ToString().ToLower(),
            ReviewedBy = reviewedBy,
            RejectionReason = rejectionReason,
            ReviewedAt = reviewedAt
        });
    }

    public async Task<bool> HasPendingApplicationAsync(Guid userId, Guid unitId, CancellationToken ct = default)
    {
        using var conn = _factory.CreateUserConnection();
        const string sql = """
            SELECT EXISTS (
                SELECT 1 FROM public.applications
                WHERE applicant_user_id = @UserId AND unit_id = @UnitId AND application_status = 'pending'
            )
            """;
        return await conn.QuerySingleAsync<bool>(sql, new { UserId = userId, UnitId = unitId });
    }

    private record ApplicationRow(
        Guid ApplicationId,
        string ApplicantName,
        string? ApplicantPhone,
        string UnitNumber,
        string BlockName,
        string ResidentType,
        DateTime SubmittedAt,
        bool DuplicateWarning,
        long TotalCount
    );
}
