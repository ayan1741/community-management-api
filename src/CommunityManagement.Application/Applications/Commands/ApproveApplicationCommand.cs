using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Applications.Commands;

public record ApproveApplicationCommand(Guid OrgId, Guid ApplicationId) : IRequest;

public class ApproveApplicationCommandHandler : IRequestHandler<ApproveApplicationCommand>
{
    private readonly IApplicationRepository _applications;
    private readonly IMemberRepository _members;
    private readonly IUnitResidentRepository _unitResidents;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public ApproveApplicationCommandHandler(
        IApplicationRepository applications,
        IMemberRepository members,
        IUnitResidentRepository unitResidents,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _applications = applications;
        _members = members;
        _unitResidents = unitResidents;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task Handle(ApproveApplicationCommand request, CancellationToken ct)
    {
        // Okuma işlemleri — repo'lar üzerinden
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var application = await _applications.GetByIdAsync(request.ApplicationId, ct)
            ?? throw AppException.NotFound("Başvuru bulunamadı.");

        if (application.OrganizationId != request.OrgId)
            throw AppException.Forbidden("Bu başvuruya erişim izniniz yok.");

        if (application.ApplicationStatus != ApplicationStatus.Pending)
            throw AppException.UnprocessableEntity("Yalnızca bekleyen başvurular onaylanabilir.");

        var existingMember = await _members.GetByUserIdAsync(request.OrgId, application.ApplicantUserId, ct);
        var currentResidents = await _unitResidents.GetByUnitIdAsync(application.UnitId, ct);

        var now = DateTimeOffset.UtcNow;
        var memberId = existingMember?.Id ?? Guid.NewGuid();

        // Yazma işlemleri — tek transaction içinde
        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // a. Başvuru durumunu güncelle
            await conn.ExecuteAsync(
                """
                UPDATE public.applications
                SET application_status = 'approved',
                    reviewed_by = @ReviewedBy,
                    rejection_reason = NULL,
                    reviewed_at = @ReviewedAt,
                    updated_at = now()
                WHERE id = @ApplicationId
                """,
                new
                {
                    request.ApplicationId,
                    ReviewedBy = _currentUser.UserId,
                    ReviewedAt = now.UtcDateTime,
                }, tx);

            // b. Üyelik kaydı oluştur/güncelle
            await conn.ExecuteAsync(
                """
                INSERT INTO public.organization_members
                  (id, organization_id, user_id, role, status, invited_by, created_at, updated_at)
                VALUES
                  (@Id, @OrganizationId, @UserId, 'resident', 'active', NULL, @CreatedAt, @UpdatedAt)
                ON CONFLICT (organization_id, user_id) DO UPDATE
                SET role = EXCLUDED.role,
                    status = EXCLUDED.status,
                    updated_at = now()
                """,
                new
                {
                    Id = memberId,
                    OrganizationId = request.OrgId,
                    UserId = application.ApplicantUserId,
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime,
                }, tx);

            // c. Daire sakini kaydı oluştur (ON CONFLICT DO NOTHING ile idempotent)
            await conn.ExecuteAsync(
                """
                INSERT INTO public.unit_residents
                    (id, unit_id, user_id, organization_id, resident_type, is_primary, status)
                VALUES
                    (@Id, @UnitId, @UserId, @OrganizationId, @ResidentType, @IsPrimary, 'active')
                ON CONFLICT (unit_id, user_id) WHERE status = 'active' DO NOTHING
                """,
                new
                {
                    Id = Guid.NewGuid(),
                    application.UnitId,
                    UserId = application.ApplicantUserId,
                    application.OrganizationId,
                    ResidentType = application.ApplicantResidentType.ToString().ToLower(),
                    IsPrimary = currentResidents.Count == 0,
                }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
