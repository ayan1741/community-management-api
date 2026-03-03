using CommunityManagement.Application.Common;
using CommunityManagement.Core.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using Dapper;
using MediatR;

namespace CommunityManagement.Application.Applications.Commands;

public record SubmitApplicationCommand(
    string? InvitationCode,
    Guid? OrganizationId,
    Guid? UnitId,
    ResidentType ResidentType
) : IRequest<SubmitApplicationResult>;

public record SubmitApplicationResult(
    Guid ApplicationId,
    string Status,
    string Message
);

public class SubmitApplicationCommandHandler : IRequestHandler<SubmitApplicationCommand, SubmitApplicationResult>
{
    private readonly IApplicationRepository _applications;
    private readonly IInvitationRepository _invitations;
    private readonly IMemberRepository _members;
    private readonly IProfileRepository _profiles;
    private readonly IUnitResidentRepository _unitResidents;
    private readonly ICurrentUserService _currentUser;
    private readonly IDbConnectionFactory _factory;

    public SubmitApplicationCommandHandler(
        IApplicationRepository applications,
        IInvitationRepository invitations,
        IMemberRepository members,
        IProfileRepository profiles,
        IUnitResidentRepository unitResidents,
        ICurrentUserService currentUser,
        IDbConnectionFactory factory)
    {
        _applications = applications;
        _invitations = invitations;
        _members = members;
        _profiles = profiles;
        _unitResidents = unitResidents;
        _currentUser = currentUser;
        _factory = factory;
    }

    public async Task<SubmitApplicationResult> Handle(SubmitApplicationCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId;

        var profile = await _profiles.GetByIdAsync(userId, ct)
            ?? throw AppException.NotFound("Kullanıcı profili bulunamadı.");

        if (profile.KvkkConsentAt is null)
            throw AppException.UnprocessableEntity("KVKK onayı verilmeden başvuru yapılamaz.");

        if (!string.IsNullOrWhiteSpace(request.InvitationCode))
            return await HandleInvitedFlow(request, userId, ct);

        return await HandleCodeslessFlow(request, userId, ct);
    }

    private async Task<SubmitApplicationResult> HandleInvitedFlow(
        SubmitApplicationCommand request, Guid userId, CancellationToken ct)
    {
        // Okuma işlemleri — repo'lar üzerinden
        var invitation = await _invitations.GetByCodeAsync(request.InvitationCode!.ToUpperInvariant(), ct)
            ?? throw AppException.UnprocessableEntity("Geçersiz davet kodu.");

        if (invitation.CodeStatus != CodeStatus.Active || invitation.ExpiresAt < DateTimeOffset.UtcNow)
            throw AppException.UnprocessableEntity("Davet kodunun süresi dolmuş veya geçersiz.");

        var existingMember = await _members.GetByUserIdAsync(invitation.OrganizationId, userId, ct);
        var currentResidents = await _unitResidents.GetByUnitIdAsync(invitation.UnitId, ct);

        var applicationId = Guid.NewGuid();
        var memberId = existingMember?.Id ?? Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var memberStatus = existingMember is not null ? MemberStatus.Active : MemberStatus.Active;

        // Yazma işlemleri — tek transaction içinde
        await using var conn = _factory.CreateServiceRoleDbConnection();
        await conn.OpenAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        try
        {
            // a. Başvuru kaydı oluştur
            await conn.ExecuteAsync(
                """
                INSERT INTO public.applications
                  (id, organization_id, unit_id, invitation_id, applicant_user_id,
                   applicant_resident_type, application_status, rejection_reason,
                   reviewed_by, reviewed_at, created_at, updated_at)
                VALUES
                  (@Id, @OrganizationId, @UnitId, @InvitationId, @ApplicantUserId,
                   @ApplicantResidentType, @ApplicationStatus, NULL,
                   NULL, @ReviewedAt, @CreatedAt, @UpdatedAt)
                """,
                new
                {
                    Id = applicationId,
                    invitation.OrganizationId,
                    invitation.UnitId,
                    InvitationId = invitation.Id,
                    ApplicantUserId = userId,
                    ApplicantResidentType = request.ResidentType.ToString().ToLower(),
                    ApplicationStatus = "approved",
                    ReviewedAt = now.UtcDateTime,
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime,
                }, tx);

            // b. Davet kodunu kullanıldı olarak işaretle
            await conn.ExecuteAsync(
                "UPDATE public.invitation_codes SET code_status = 'used', updated_at = now() WHERE id = @Id",
                new { invitation.Id }, tx);

            // c. Üyelik kaydı oluştur/güncelle
            await conn.ExecuteAsync(
                """
                INSERT INTO public.organization_members
                  (id, organization_id, user_id, role, status, invited_by, created_at, updated_at)
                VALUES
                  (@Id, @OrganizationId, @UserId, 'resident', @Status, @InvitedBy, @CreatedAt, @UpdatedAt)
                ON CONFLICT (organization_id, user_id) DO UPDATE
                SET role = EXCLUDED.role,
                    status = EXCLUDED.status,
                    updated_at = now()
                """,
                new
                {
                    Id = memberId,
                    invitation.OrganizationId,
                    UserId = userId,
                    Status = memberStatus.ToString().ToLower(),
                    InvitedBy = invitation.CreatedBy,
                    CreatedAt = now.UtcDateTime,
                    UpdatedAt = now.UtcDateTime,
                }, tx);

            // d. Daire sakini kaydı oluştur (ON CONFLICT DO NOTHING ile idempotent)
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
                    invitation.UnitId,
                    UserId = userId,
                    invitation.OrganizationId,
                    ResidentType = request.ResidentType.ToString().ToLower(),
                    IsPrimary = currentResidents.Count == 0,
                }, tx);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        return new SubmitApplicationResult(applicationId, "approved", "Davet kabul edildi. Hoş geldiniz!");
    }

    private async Task<SubmitApplicationResult> HandleCodeslessFlow(
        SubmitApplicationCommand request, Guid userId, CancellationToken ct)
    {
        if (request.OrganizationId is null || request.UnitId is null)
            throw AppException.UnprocessableEntity("Kodsuz başvuru için organizasyon ve daire bilgisi gereklidir.");

        var hasPending = await _applications.HasPendingApplicationAsync(userId, request.UnitId.Value, ct);
        if (hasPending)
            throw AppException.UnprocessableEntity("Bu daire için zaten bekleyen bir başvurunuz var.");

        var application = new Core.Entities.Application
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrganizationId.Value,
            UnitId = request.UnitId.Value,
            InvitationId = null,
            ApplicantUserId = userId,
            ApplicantResidentType = request.ResidentType,
            ApplicationStatus = ApplicationStatus.Pending,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _applications.CreateAsync(application, ct);

        return new SubmitApplicationResult(application.Id, "pending", "Başvurunuz alındı. Yönetici onayı bekleniyor.");
    }
}
