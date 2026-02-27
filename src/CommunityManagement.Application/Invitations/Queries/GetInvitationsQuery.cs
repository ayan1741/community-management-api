using CommunityManagement.Application.Common;
using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Invitations.Queries;

public record GetInvitationsQuery(
    Guid OrgId,
    CodeStatus? Status,
    Guid? UnitId,
    int Page,
    int PageSize
) : IRequest<GetInvitationsResult>;

public record GetInvitationsResult(
    IReadOnlyList<InvitationListItem> Items,
    int TotalCount,
    int Page,
    int PageSize
);

public class GetInvitationsQueryHandler : IRequestHandler<GetInvitationsQuery, GetInvitationsResult>
{
    private readonly IInvitationRepository _invitations;
    private readonly ICurrentUserService _currentUser;

    public GetInvitationsQueryHandler(IInvitationRepository invitations, ICurrentUserService currentUser)
    {
        _invitations = invitations;
        _currentUser = currentUser;
    }

    public async Task<GetInvitationsResult> Handle(GetInvitationsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Admin, ct);

        var memberStatus = await _currentUser.GetMembershipStatusAsync(request.OrgId, ct);
        if (memberStatus == MemberStatus.Suspended)
            throw AppException.Forbidden("Hesabınız bu organizasyonda askıya alınmış.");

        var (items, totalCount) = await _invitations.GetByOrgIdAsync(
            request.OrgId, request.Status, request.UnitId,
            request.Page, request.PageSize, ct);

        return new GetInvitationsResult(items, totalCount, request.Page, request.PageSize);
    }
}
