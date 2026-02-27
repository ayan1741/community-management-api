using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Queries;

public record GetMemberHistoryQuery(Guid OrgId, Guid TargetUserId) : IRequest<IReadOnlyList<MemberHistoryItem>>;

public class GetMemberHistoryQueryHandler : IRequestHandler<GetMemberHistoryQuery, IReadOnlyList<MemberHistoryItem>>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public GetMemberHistoryQueryHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<MemberHistoryItem>> Handle(GetMemberHistoryQuery request, CancellationToken ct)
    {
        var isOwnHistory = _currentUser.UserId == request.TargetUserId;
        if (!isOwnHistory)
            await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended && !isOwnHistory)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        return await _members.GetHistoryAsync(request.OrgId, request.TargetUserId, ct);
    }
}
