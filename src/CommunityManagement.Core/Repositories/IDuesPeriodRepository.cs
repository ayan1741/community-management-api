using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IDuesPeriodRepository
{
    Task<IReadOnlyList<DuesPeriodListItem>> GetByOrgIdAsync(Guid orgId, string? status = null, CancellationToken ct = default);
    Task<DuesPeriod?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasAccrualsAsync(Guid periodId, CancellationToken ct = default);
    Task<DuesPeriod> CreateAsync(DuesPeriod period, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid periodId, string status, DateTimeOffset? closedAt, CancellationToken ct = default);
    Task DeleteAsync(Guid periodId, CancellationToken ct = default);
}

public record DuesPeriodListItem(
    Guid Id,
    string Name,
    DateOnly StartDate,
    DateOnly DueDate,
    string Status,
    long TotalDues,         // COUNT() → long (CLAUDE.md kuralı)
    long PaidCount,         // COUNT() → long
    long PendingCount,      // COUNT() → long
    decimal TotalAmount,
    decimal CollectedAmount,
    DateTimeOffset CreatedAt
);
