using CommunityManagement.Application.Common;
using CommunityManagement.Core.Repositories;
using MediatR;

namespace CommunityManagement.Application.Invitations.Queries;

public record ValidateInvitationCodeQuery(string Code) : IRequest<ValidateInvitationCodeResult>;

public record ValidateInvitationCodeResult(
    bool Valid,
    string? Reason,
    string? OrganizationName,
    string? BlockName,
    string? UnitNumber,
    DateTimeOffset? ExpiresAt
);

public class ValidateInvitationCodeQueryHandler : IRequestHandler<ValidateInvitationCodeQuery, ValidateInvitationCodeResult>
{
    private readonly IInvitationRepository _invitations;

    public ValidateInvitationCodeQueryHandler(IInvitationRepository invitations)
    {
        _invitations = invitations;
    }

    public async Task<ValidateInvitationCodeResult> Handle(ValidateInvitationCodeQuery request, CancellationToken ct)
    {
        var invitation = await _invitations.GetByCodeWithDetailsAsync(request.Code.ToUpperInvariant(), ct);

        if (invitation is null)
            return new ValidateInvitationCodeResult(false, "not_found", null, null, null, null);

        if (invitation.CodeStatus == "used")
            return new ValidateInvitationCodeResult(false, "used", null, null, null, null);

        if (invitation.CodeStatus is "revoked" or "expired" || invitation.ExpiresAt < DateTimeOffset.UtcNow)
            return new ValidateInvitationCodeResult(false, "expired", null, null, null, null);

        return new ValidateInvitationCodeResult(
            true, null,
            invitation.OrganizationName,
            invitation.BlockName,
            invitation.UnitNumber,
            invitation.ExpiresAt);
    }
}
