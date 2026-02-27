using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Applications.Commands;

public record RejectApplicationCommand(Guid OrgId, Guid ApplicationId, string? Reason) : IRequest;

public class RejectApplicationCommandHandler : IRequestHandler<RejectApplicationCommand>
{
    private readonly IApplicationRepository _applications;
    private readonly ICurrentUserService _currentUser;

    public RejectApplicationCommandHandler(IApplicationRepository applications, ICurrentUserService currentUser)
    {
        _applications = applications;
        _currentUser = currentUser;
    }

    public async Task Handle(RejectApplicationCommand request, CancellationToken ct)
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
            throw AppException.UnprocessableEntity("Yalnızca bekleyen başvurular reddedilebilir.");

        await _applications.UpdateStatusAsync(
            request.ApplicationId, ApplicationStatus.Rejected,
            _currentUser.UserId, request.Reason, DateTimeOffset.UtcNow, ct);
    }
}
