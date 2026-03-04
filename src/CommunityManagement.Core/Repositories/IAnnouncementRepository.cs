using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public record AnnouncementListItem(
    Guid Id, string Title, string Category, string Priority,
    string Status, bool IsPinned,
    string CreatedByName, DateTimeOffset? PublishedAt, DateTimeOffset CreatedAt,
    bool IsRead, int TotalCount);

public record AnnouncementDetail(
    Guid Id, Guid OrganizationId, string Title, string Body,
    string Category, string Priority, string TargetType, string? TargetIds,
    string Status, bool IsPinned,
    DateTimeOffset? PublishedAt, DateTimeOffset? ExpiresAt,
    string? AttachmentUrls, int? TargetMemberCount,
    string CreatedByName, Guid CreatedBy, Guid? UpdatedBy,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    bool IsRead);

public record AnnouncementReadItem(
    Guid UserId, string FullName, DateTimeOffset ReadAt);

public record AnnouncementUnreadItem(
    Guid UserId, string FullName);

public interface IAnnouncementRepository
{
    Task<Announcement?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AnnouncementDetail?> GetDetailAsync(Guid id, Guid currentUserId, CancellationToken ct = default);

    Task<(IReadOnlyList<AnnouncementListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, Guid currentUserId,
        string? category, string? priority, string? status,
        bool includeExpired, bool includeDrafts, bool filterByTarget,
        int page, int pageSize, CancellationToken ct = default);

    Task<int> GetPinnedCountAsync(Guid orgId, CancellationToken ct = default);

    Task<(IReadOnlyList<AnnouncementReadItem> Readers, int TotalCount)> GetReadersAsync(
        Guid announcementId, int page, int pageSize, CancellationToken ct = default);

    Task<(IReadOnlyList<AnnouncementUnreadItem> NonReaders, int TotalCount)> GetNonReadersAsync(
        Guid announcementId, int page, int pageSize, CancellationToken ct = default);

    Task<int> GetReadCountAsync(Guid announcementId, CancellationToken ct = default);
}
