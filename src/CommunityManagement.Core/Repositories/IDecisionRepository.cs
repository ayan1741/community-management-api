using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

// --- Decision List Item ---
public record DecisionListDto(
    Guid Id, string Title, string Status,
    string? AgendaItemTitle, string? PollTitle,
    string DecidedByName,
    DateTimeOffset DecidedAt, long TotalCount);

// --- Decision Detail ---
public record DecisionDetailDto(
    Guid Id, Guid OrganizationId,
    Guid? AgendaItemId, string? AgendaItemTitle,
    Guid? PollId, string? PollTitle,
    string Title, string Description, string Status,
    string DecidedByName,
    DateTimeOffset DecidedAt,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public interface IDecisionRepository
{
    Task<Decision?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<DecisionDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<DecisionListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId,
        string? status,
        DateTimeOffset? fromDate, DateTimeOffset? toDate,
        int page, int pageSize, CancellationToken ct = default);
}
