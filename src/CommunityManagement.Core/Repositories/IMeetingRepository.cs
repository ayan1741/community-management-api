using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

// --- Meeting List Item ---
public record MeetingListDto(
    Guid Id, string Title, string Status,
    DateTimeOffset MeetingDate,
    int AgendaItemCount,
    DateTimeOffset CreatedAt, long TotalCount);

// --- Meeting Detail ---
public record MeetingDetailDto(
    Guid Id, Guid OrganizationId,
    string Title, string Description, string Status,
    DateTimeOffset MeetingDate,
    string CreatedByName,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IMeetingRepository
{
    Task<Meeting?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<MeetingDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<MeetingListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId,
        string? status,
        int page, int pageSize, CancellationToken ct = default);
}
