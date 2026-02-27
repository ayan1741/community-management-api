using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Commands;

public record SuspendMemberCommand(Guid OrgId, Guid TargetUserId) : IRequest;

public class SuspendMemberCommandHandler : IRequestHandler<SuspendMemberCommand>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public SuspendMemberCommandHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task Handle(SuspendMemberCommand request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var target = await _members.GetByUserIdAsync(request.OrgId, request.TargetUserId, ct)
            ?? throw AppException.NotFound("Üye bulunamadı.");

        if (target.Role == MemberRole.Admin)
            throw AppException.UnprocessableEntity("Yöneticiler askıya alınamaz.");

        if (target.Status == MemberStatus.Suspended)
            throw AppException.UnprocessableEntity("Üye zaten askıya alınmış.");

        await _members.UpdateStatusAsync(
            request.OrgId, request.TargetUserId,
            MemberStatus.Suspended, _currentUser.UserId, ct);
    }
}
