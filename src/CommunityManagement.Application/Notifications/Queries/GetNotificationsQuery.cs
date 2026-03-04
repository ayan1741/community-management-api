using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Notifications.Queries;

public record GetNotificationsQuery(
    Guid OrgId, bool? IsRead, string? Type,
    int Page, int PageSize
) : IRequest<GetNotificationsResult>;

public record GetNotificationsResult(
    IReadOnlyList<NotificationListItem> Items, int TotalCount, int Page, int PageSize);

public class GetNotificationsQueryHandler : IRequestHandler<GetNotificationsQuery, GetNotificationsResult>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;

    public GetNotificationsQueryHandler(INotificationRepository notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<GetNotificationsResult> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        var userId = _currentUser.UserId;
        var (items, totalCount) = await _notifications.GetByUserAsync(
            userId, request.OrgId, request.IsRead, request.Type,
            request.Page, request.PageSize, ct);

        return new GetNotificationsResult(items, totalCount, request.Page, request.PageSize);
    }
}
