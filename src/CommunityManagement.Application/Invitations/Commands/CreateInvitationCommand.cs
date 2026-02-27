using CommunityManagement.Application.Common;
using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;
using System.Security.Cryptography;
using System.Text;

namespace CommunityManagement.Application.Invitations.Commands;

public record CreateInvitationCommand(
    Guid OrgId,
    Guid UnitId,
    int ExpiresInDays
) : IRequest<CreateInvitationResult>;

public record CreateInvitationResult(
    Guid InvitationId,
    string InvitationCode,
    DateTimeOffset ExpiresAt
);

public class CreateInvitationCommandHandler : IRequestHandler<CreateInvitationCommand, CreateInvitationResult>
{
    private readonly IInvitationRepository _invitations;
    private readonly ICurrentUserService _currentUser;

    public CreateInvitationCommandHandler(IInvitationRepository invitations, ICurrentUserService currentUser)
    {
        _invitations = invitations;
        _currentUser = currentUser;
    }

    public async Task<CreateInvitationResult> Handle(CreateInvitationCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var hasActive = await _invitations.HasActiveInvitationForUnitAsync(request.UnitId, ct);
        if (hasActive)
            throw AppException.UnprocessableEntity("Bu daire için zaten aktif bir davet kodu mevcut.");

        var code = GenerateCode();
        var expiresAt = DateTimeOffset.UtcNow.AddDays(request.ExpiresInDays);

        var invitation = new Invitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = request.OrgId,
            UnitId = request.UnitId,
            InvitationCode = code,
            CodeStatus = CodeStatus.Active,
            CreatedBy = _currentUser.UserId,
            ExpiresAt = expiresAt,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _invitations.CreateAsync(invitation, ct);

        return new CreateInvitationResult(invitation.Id, code, expiresAt);
    }

    private static string GenerateCode()
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var bytes = RandomNumberGenerator.GetBytes(8);
        var sb = new StringBuilder(8);
        foreach (var b in bytes)
            sb.Append(alphabet[b % alphabet.Length]);
        return sb.ToString();
    }
}
