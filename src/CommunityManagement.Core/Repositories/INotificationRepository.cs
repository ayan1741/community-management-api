namespace CommunityManagement.Core.Repositories;

public record NotificationListItem(
    Guid Id, string Type, string Title, string? Body,
    string? ReferenceType, Guid? ReferenceId,
    bool IsRead, DateTimeOffset? ReadAt, DateTimeOffset CreatedAt,
    int TotalCount);

public interface INotificationRepository
{
    Task<(IReadOnlyList<NotificationListItem> Items, int TotalCount)> GetByUserAsync(
        Guid userId, Guid orgId, bool? isRead, string? type,
        int page, int pageSize, CancellationToken ct = default);

    Task<int> GetUnreadCountAsync(Guid userId, Guid orgId, CancellationToken ct = default);

    Task MarkReadAsync(Guid userId, Guid orgId, IReadOnlyList<Guid>? notificationIds, CancellationToken ct = default);

    Task DeleteByReferenceAsync(Guid referenceId, string referenceType, CancellationToken ct = default);
}
