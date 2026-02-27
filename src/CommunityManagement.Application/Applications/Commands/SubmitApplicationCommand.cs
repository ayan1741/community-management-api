using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using CommunityManagement.Core.Entities;
using ApplicationEntity = CommunityManagement.Core.Entities.Application;

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
    private readonly ICurrentUserService _currentUser;

    public SubmitApplicationCommandHandler(
        IApplicationRepository applications,
        IInvitationRepository invitations,
        IMemberRepository members,
        IProfileRepository profiles,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _invitations = invitations;
        _members = members;
        _profiles = profiles;
        _currentUser = currentUser;
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
        var invitation = await _invitations.GetByCodeAsync(request.InvitationCode!.ToUpperInvariant(), ct)
            ?? throw AppException.UnprocessableEntity("Geçersiz davet kodu.");

        if (invitation.CodeStatus != CodeStatus.Active || invitation.ExpiresAt < DateTimeOffset.UtcNow)
            throw AppException.UnprocessableEntity("Davet kodunun süresi dolmuş veya geçersiz.");

        var application = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            OrganizationId = invitation.OrganizationId,
            UnitId = invitation.UnitId,
            InvitationId = invitation.Id,
            ApplicantUserId = userId,
            ApplicantResidentType = request.ResidentType,
            ApplicationStatus = ApplicationStatus.Approved,
            ReviewedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _applications.CreateAsync(application, ct);
        await _invitations.UpdateStatusAsync(invitation.Id, CodeStatus.Used, ct);

        var existingMember = await _members.GetByUserIdAsync(invitation.OrganizationId, userId, ct);
        var member = existingMember ?? new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = invitation.OrganizationId,
            UserId = userId,
            Role = MemberRole.Resident,
            Status = MemberStatus.Active,
            InvitedBy = invitation.CreatedBy,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (existingMember is not null)
            member.Status = MemberStatus.Active;

        await _members.UpsertAsync(member, ct);

        return new SubmitApplicationResult(application.Id, "approved", "Davet kabul edildi. Hoş geldiniz!");
    }

    private async Task<SubmitApplicationResult> HandleCodeslessFlow(
        SubmitApplicationCommand request, Guid userId, CancellationToken ct)
    {
        if (request.OrganizationId is null || request.UnitId is null)
            throw AppException.UnprocessableEntity("Kodsuz başvuru için organizasyon ve daire bilgisi gereklidir.");

        var hasPending = await _applications.HasPendingApplicationAsync(userId, request.UnitId.Value, ct);
        if (hasPending)
            throw AppException.UnprocessableEntity("Bu daire için zaten bekleyen bir başvurunuz var.");

        var application = new ApplicationEntity
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
