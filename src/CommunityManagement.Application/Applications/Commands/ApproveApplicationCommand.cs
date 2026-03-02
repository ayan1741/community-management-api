using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Applications.Commands;

public record ApproveApplicationCommand(Guid OrgId, Guid ApplicationId) : IRequest;

public class ApproveApplicationCommandHandler : IRequestHandler<ApproveApplicationCommand>
{
    private readonly IApplicationRepository _applications;
    private readonly IMemberRepository _members;
    private readonly IUnitResidentRepository _unitResidents;
    private readonly ICurrentUserService _currentUser;

    public ApproveApplicationCommandHandler(
        IApplicationRepository applications,
        IMemberRepository members,
        IUnitResidentRepository unitResidents,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _members = members;
        _unitResidents = unitResidents;
        _currentUser = currentUser;
    }

    public async Task Handle(ApproveApplicationCommand request, CancellationToken ct)
    {
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

        var reviewedAt = DateTimeOffset.UtcNow;
        await _applications.UpdateStatusAsync(
            request.ApplicationId, ApplicationStatus.Approved,
            _currentUser.UserId, null, reviewedAt, ct);

        var existingMember = await _members.GetByUserIdAsync(request.OrgId, application.ApplicantUserId, ct);
        var member = existingMember ?? new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            UserId = application.ApplicantUserId,
            Role = MemberRole.Resident,
            Status = MemberStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (existingMember is not null)
            member.Status = MemberStatus.Active;

        await _members.UpsertAsync(member, ct);

        // unit_residents kaydı oluştur (ON CONFLICT DO NOTHING ile idempotent)
        var currentResidents = await _unitResidents.GetByUnitIdAsync(application.UnitId, ct);
        await _unitResidents.CreateAsync(new UnitResident
        {
            Id = Guid.NewGuid(),
            UnitId = application.UnitId,
            UserId = application.ApplicantUserId,
            OrganizationId = application.OrganizationId,
            ResidentType = application.ApplicantResidentType,
            IsPrimary = currentResidents.Count == 0,
            Status = UnitResidentStatus.Active
        }, ct);
    }
}
