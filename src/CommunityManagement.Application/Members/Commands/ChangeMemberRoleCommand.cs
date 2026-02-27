using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Commands;

public record ChangeMemberRoleCommand(Guid OrgId, Guid TargetUserId, MemberRole NewRole) : IRequest;

public class ChangeMemberRoleCommandHandler : IRequestHandler<ChangeMemberRoleCommand>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public ChangeMemberRoleCommandHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task Handle(ChangeMemberRoleCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var target = await _members.GetByUserIdAsync(request.OrgId, request.TargetUserId, ct)
            ?? throw AppException.NotFound("Üye bulunamadı.");

        // Prevent downgrading the last admin
        if (target.Role == MemberRole.Admin && request.NewRole != MemberRole.Admin)
        {
            var isLastAdmin = await _members.IsLastAdminAsync(request.OrgId, request.TargetUserId, ct);
            if (isLastAdmin)
                throw AppException.UnprocessableEntity(
                    "Bu organizasyonun tek yöneticisisiniz. Rol değiştirmek için önce başka bir admin atayın.");
        }

        await _members.UpdateRoleAsync(request.OrgId, request.TargetUserId, request.NewRole, ct);
    }
}
