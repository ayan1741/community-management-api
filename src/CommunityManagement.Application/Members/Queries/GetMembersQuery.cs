using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Members.Queries;

public record GetMembersQuery(
    Guid OrgId,
    MemberStatus? Status,
    MemberRole? Role,
    int Page,
    int PageSize
) : IRequest<GetMembersResult>;

public record GetMembersResult(
    IReadOnlyList<MemberListItem> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public class GetMembersQueryHandler : IRequestHandler<GetMembersQuery, GetMembersResult>
{
    private readonly IMemberRepository _members;
    private readonly ICurrentUserService _currentUser;

    public GetMembersQueryHandler(IMemberRepository members, ICurrentUserService currentUser)
    {
        _members = members;
        _currentUser = currentUser;
    }

    public async Task<GetMembersResult> Handle(GetMembersQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.BoardMember, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var (items, totalCount) = await _members.GetByOrgIdAsync(
            request.OrgId, request.Status, request.Role,
            request.Page, request.PageSize, ct);

        return new GetMembersResult(items, totalCount, request.Page, request.PageSize);
    }
}
