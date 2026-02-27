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
    private readonly ICurrentUserService _currentUser;

    public ApproveApplicationCommandHandler(
        IApplicationRepository applications,
        IMemberRepository members,
        ICurrentUserService currentUser)
    {
        _applications = applications;
        _members = members;
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
    }
}
