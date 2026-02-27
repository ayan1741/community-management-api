using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
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
        var invitation = await _invitations.GetByCodeAsync(request.Code.ToUpperInvariant(), ct);

        if (invitation is null)
            return new ValidateInvitationCodeResult(false, "not_found", null, null, null, null);

        if (invitation.CodeStatus == CodeStatus.Used)
            return new ValidateInvitationCodeResult(false, "used", null, null, null, null);

        if (invitation.CodeStatus == CodeStatus.Revoked || invitation.CodeStatus == CodeStatus.Expired
            || invitation.ExpiresAt < DateTimeOffset.UtcNow)
            return new ValidateInvitationCodeResult(false, "expired", null, null, null, null);

        // Unit/org details fetched via repository join (returned in invitation object via extended query)
        return new ValidateInvitationCodeResult(true, null, null, null, null, invitation.ExpiresAt);
    }
}
