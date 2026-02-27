using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Commands;

public record ActivateMemberCommand(Guid OrgId, Guid TargetUserId) : IRequest;

public class ActivateMemberCommandHandler : IRequestHandler<ActivateMemberCommand>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public ActivateMemberCommandHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task Handle(ActivateMemberCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var target = await _members.GetByUserIdAsync(request.OrgId, request.TargetUserId, ct)
            ?? throw AppException.NotFound("Üye bulunamadı.");

        if (target.Status != MemberStatus.Suspended)
            throw AppException.UnprocessableEntity("Üye askıya alınmış durumda değil.");

        await _members.UpdateStatusAsync(
            request.OrgId, request.TargetUserId,
            MemberStatus.Active, null, ct);
    }
}
