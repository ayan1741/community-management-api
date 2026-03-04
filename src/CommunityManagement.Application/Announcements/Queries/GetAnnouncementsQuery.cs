using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Announcements.Queries;

public record GetAnnouncementsQuery(
    Guid OrgId,
    string? Category, string? Priority, string? Status,
    int Page, int PageSize
) : IRequest<GetAnnouncementsResult>;

public record GetAnnouncementsResult(
    IReadOnlyList<AnnouncementListItem> Items, int TotalCount, int Page, int PageSize);

public class GetAnnouncementsQueryHandler : IRequestHandler<GetAnnouncementsQuery, GetAnnouncementsResult>
{
    private readonly IAnnouncementRepository _announcements;
    private readonly ICurrentUserService _currentUser;

    public GetAnnouncementsQueryHandler(IAnnouncementRepository announcements, ICurrentUserService currentUser)
    {
        _announcements = announcements;
        _currentUser = currentUser;
    }

    public async Task<GetAnnouncementsResult> Handle(GetAnnouncementsQuery request, CancellationToken ct)
    {
        var role = await _currentUser.GetRoleAsync(request.OrgId, ct);
        var isAdminOrBoard = role is MemberRole.Admin or MemberRole.BoardMember;

        var (items, totalCount) = await _announcements.GetByOrgIdAsync(
            request.OrgId, _currentUser.UserId,
            request.Category, request.Priority, request.Status,
            includeExpired: isAdminOrBoard,
            includeDrafts: isAdminOrBoard,
            filterByTarget: !isAdminOrBoard,
            request.Page, request.PageSize, ct);

        return new GetAnnouncementsResult(items, totalCount, request.Page, request.PageSize);
    }
}
