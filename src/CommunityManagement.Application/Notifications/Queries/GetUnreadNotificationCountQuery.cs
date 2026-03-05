using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Notifications.Queries;

public record GetUnreadNotificationCountQuery(Guid OrgId) : IRequest<int>;

public class GetUnreadNotificationCountQueryHandler : IRequestHandler<GetUnreadNotificationCountQuery, int>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;

    public GetUnreadNotificationCountQueryHandler(INotificationRepository notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task<int> Handle(GetUnreadNotificationCountQuery request, CancellationToken ct)
    {
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        var userId = _currentUser.UserId;
        return await _notifications.GetUnreadCountAsync(userId, request.OrgId, ct);
    }
}
