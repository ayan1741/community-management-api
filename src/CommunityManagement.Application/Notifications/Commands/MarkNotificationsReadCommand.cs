using CommunityManagement.Core.Enums;
using CommunityManagement.Core.Repositories;
using CommunityManagement.Core.Services;
using MediatR;

namespace CommunityManagement.Application.Notifications.Commands;

public record MarkNotificationsReadCommand(
    Guid OrgId, List<Guid>? NotificationIds
) : IRequest;

public class MarkNotificationsReadCommandHandler : IRequestHandler<MarkNotificationsReadCommand>
{
    private readonly INotificationRepository _notifications;
    private readonly ICurrentUserService _currentUser;

    public MarkNotificationsReadCommandHandler(
        INotificationRepository notifications, ICurrentUserService currentUser)
    {
        _notifications = notifications;
        _currentUser = currentUser;
    }

    public async Task Handle(MarkNotificationsReadCommand request, CancellationToken ct)
    {
        // Org üyelik kontrolü
        await _currentUser.RequireRoleAsync(request.OrgId, MemberRole.Resident, ct);
        var userId = _currentUser.UserId;
        await _notifications.MarkReadAsync(userId, request.OrgId, request.NotificationIds, ct);
    }
}
