using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

// --- Agenda List Item ---
public record AgendaItemListDto(
    Guid Id, string Title, string Category, string Status,
    bool IsPinned, int SupportCount, int CommentCount,
    string CreatedByName, Guid CreatedBy,
    bool HasUserSupport,
    DateTimeOffset CreatedAt, long TotalCount);

// --- Agenda Detail ---
public record AgendaItemDetailDto(
    Guid Id, Guid OrganizationId, Guid? MeetingId,
    string Title, string Description, string Category, string Status,
    bool IsPinned, string? CloseReason,
    int SupportCount, int CommentCount,
    string CreatedByName, Guid CreatedBy,
    bool HasUserSupport,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// --- Support Detail (admin only) ---
public record AgendaSupportDetailDto(
    Guid UserId, string UserName, DateTimeOffset CreatedAt);

// --- Comment Item ---
public record AgendaCommentDto(
    Guid Id, string Content, bool IsDeleted,
    string UserName, Guid UserId,
    DateTimeOffset CreatedAt);

// --- Stats ---
public record AgendaStats(
    int TotalOpen, int TotalUnderReview,
    int TotalVoting, int TotalDecided, int TotalClosed);

public interface IAgendaRepository
{
    // Agenda Items
    Task<AgendaItem?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<AgendaItemDetailDto?> GetDetailAsync(Guid id, Guid currentUserId, CancellationToken ct = default);

    Task<(IReadOnlyList<AgendaItemListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId,
        string? status, string? category, Guid? meetingId,
        string sortBy, string sortDirection,
        int page, int pageSize, CancellationToken ct = default);

    Task<AgendaStats> GetStatsAsync(Guid orgId, CancellationToken ct = default);

    // Supports
    Task<bool> HasUserSupportAsync(Guid agendaItemId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<AgendaSupportDetailDto>> GetSupportersAsync(Guid agendaItemId, CancellationToken ct = default);

    // Comments
    Task<(IReadOnlyList<AgendaCommentDto> Items, int TotalCount)> GetCommentsAsync(
        Guid agendaItemId, int page, int pageSize, CancellationToken ct = default);
    Task<AgendaComment?> GetCommentByIdAsync(Guid commentId, CancellationToken ct = default);

    // Rate limits
    Task<int> CountUserAgendaItemsTodayAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<int> CountUserAgendaItemsLastHourAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<int> CountUserCommentsLastHourAsync(Guid agendaItemId, Guid userId, CancellationToken ct = default);
}
