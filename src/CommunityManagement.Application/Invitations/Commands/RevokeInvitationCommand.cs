using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Invitations.Commands;

public record RevokeInvitationCommand(Guid OrgId, Guid InvitationId) : IRequest;

public class RevokeInvitationCommandHandler : IRequestHandler<RevokeInvitationCommand>
{
    private readonly IInvitationRepository _invitations;
    private readonly ICurrentUserService _currentUser;

    public RevokeInvitationCommandHandler(IInvitationRepository invitations, ICurrentUserService currentUser)
    {
        _invitations = invitations;
        _currentUser = currentUser;
    }

    public async Task Handle(RevokeInvitationCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var invitation = await _invitations.GetByIdAsync(request.InvitationId, ct)
            ?? throw AppException.NotFound("Davet kodu bulunamadı.");

        if (invitation.OrganizationId != request.OrgId)
            throw AppException.Forbidden("Bu davet koduna erişim izniniz yok.");

        if (invitation.CodeStatus == CodeStatus.Used)
            throw AppException.UnprocessableEntity("Kullanılmış davet kodları iptal edilemez.");

        await _invitations.UpdateStatusAsync(request.InvitationId, CodeStatus.Revoked, ct);
    }
}
