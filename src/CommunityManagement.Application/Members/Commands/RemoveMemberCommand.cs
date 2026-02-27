using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Commands;

public record RemoveMemberCommand(Guid OrgId, Guid TargetUserId) : IRequest;

public class RemoveMemberCommandHandler : IRequestHandler<RemoveMemberCommand>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public RemoveMemberCommandHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task Handle(RemoveMemberCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var target = await _members.GetByUserIdAsync(request.OrgId, request.TargetUserId, ct)
            ?? throw AppException.NotFound("Üye bulunamadı.");

        if (target.Role == MemberRole.Admin)
        {
            var isLastAdmin = await _members.IsLastAdminAsync(request.OrgId, request.TargetUserId, ct);
            if (isLastAdmin)
                throw AppException.UnprocessableEntity(
                    "Son yönetici organizasyondan çıkarılamaz.");
        }

        await _members.RemoveAsync(request.OrgId, request.TargetUserId, ct);
    }
}
