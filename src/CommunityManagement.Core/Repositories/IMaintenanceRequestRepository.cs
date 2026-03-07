using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

// --- List Item DTO ---
public record MaintenanceRequestListItem(
    Guid Id, string Title, string Category, string Priority,
    string Status, string LocationType, string? LocationNote,
    string ReportedByName, int PhotoCount,
    bool IsRecurring, bool SlaBreached,
    DateTimeOffset CreatedAt, long TotalCount);

// --- Detail DTO ---
public record MaintenanceRequestDetailDto(
    Guid Id, Guid OrganizationId,
    string Title, string Description, string Category, string Priority,
    string Status, string LocationType, Guid? UnitId, string? UnitLabel,
    string? LocationNote,
    string? AssigneeName, string? AssigneePhone, string? AssigneeNote,
    DateTimeOffset? AssignedAt,
    decimal TotalCost, bool IsRecurring,
    short? SatisfactionRating, string? SatisfactionComment, DateTimeOffset? RatedAt,
    DateTimeOffset? SlaDeadlineAt, bool SlaBreached,
    string? PhotoUrls,
    string ReportedByName, Guid ReportedBy,
    DateTimeOffset? ResolvedAt, DateTimeOffset? ClosedAt,
    DateTimeOffset? CancelledAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

// --- Timeline item ---
public record MaintenanceRequestLogItem(
    Guid Id, string? FromStatus, string ToStatus,
    string? Note, string CreatedByName, DateTimeOffset CreatedAt);

// --- Comment item ---
public record MaintenanceRequestCommentItem(
    Guid Id, string Content, string? PhotoUrl,
    string CreatedByName, Guid CreatedBy, DateTimeOffset CreatedAt);

// --- Cost item ---
public record MaintenanceRequestCostItem(
    Guid Id, decimal Amount, string? Description,
    Guid? FinanceRecordId, string CreatedByName, DateTimeOffset CreatedAt);

// --- Stats DTO ---
public record MaintenanceRequestStats(
    int TotalOpen, int TotalResolved, int TotalClosed,
    int SlaBreachedCount, int RecurringCount,
    decimal TotalCostSum);

public interface IMaintenanceRequestRepository
{
    // CRUD
    Task<MaintenanceRequest?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<MaintenanceRequestDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<MaintenanceRequestListItem> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId, string currentUserRole,
        Guid[]? currentUserUnitIds,
        string? status, string? category, string? priority, string? locationType,
        bool? isRecurring, bool? slaBreached,
        int page, int pageSize, CancellationToken ct = default);

    Task<MaintenanceRequestStats> GetStatsAsync(Guid orgId, CancellationToken ct = default);

    // Timeline
    Task<IReadOnlyList<MaintenanceRequestLogItem>> GetLogsAsync(Guid requestId, CancellationToken ct = default);

    // Comments
    Task<IReadOnlyList<MaintenanceRequestCommentItem>> GetCommentsAsync(Guid requestId, CancellationToken ct = default);

    // Costs (admin/board_member only)
    Task<IReadOnlyList<MaintenanceRequestCostItem>> GetCostsAsync(Guid requestId, CancellationToken ct = default);

    // Tekrarlayan ariza kontrolu
    Task<int> CountRecentByUnitAndCategoryAsync(
        Guid orgId, Guid? unitId, string category, int days, CancellationToken ct = default);
}
