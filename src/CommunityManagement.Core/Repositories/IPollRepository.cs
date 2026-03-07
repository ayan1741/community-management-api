using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

// --- Poll List Item ---
public record PollListDto(
    Guid Id, string Title, string PollType,
    string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    int TotalVoteCount, int TotalMemberCount,
    bool HasUserVoted,
    string? AgendaItemTitle,
    DateTimeOffset CreatedAt, long TotalCount);

// --- Poll Detail ---
public record PollDetailDto(
    Guid Id, Guid OrganizationId, Guid? AgendaItemId,
    string Title, string Description, string PollType,
    string Status, DateTimeOffset StartsAt, DateTimeOffset EndsAt,
    bool ShowInterimResults,
    int TotalMemberCount,
    string CreatedByName,
    DateTimeOffset CreatedAt);

// --- Poll Option with votes ---
public record PollOptionDto(
    Guid Id, string Label, int VoteCount, short DisplayOrder);

// --- User's own vote ---
public record UserVoteDto(Guid PollOptionId);

// --- Poll Result ---
public record PollResultDto(
    Guid PollId, string Title, string PollType, string Status,
    int TotalVoteCount, int TotalMemberCount,
    IReadOnlyList<PollOptionDto> Options);

public interface IPollRepository
{
    Task<Poll?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task<PollDetailDto?> GetDetailAsync(Guid id, CancellationToken ct = default);

    Task<(IReadOnlyList<PollListDto> Items, int TotalCount)> GetListAsync(
        Guid orgId, Guid currentUserId,
        string? status,
        int page, int pageSize, CancellationToken ct = default);

    Task<IReadOnlyList<PollOptionDto>> GetOptionsAsync(Guid pollId, CancellationToken ct = default);

    Task<UserVoteDto?> GetUserVoteAsync(Guid pollId, Guid userId, CancellationToken ct = default);

    Task<PollResultDto?> GetResultAsync(Guid pollId, CancellationToken ct = default);

    Task<int> GetTotalVoteCountAsync(Guid pollId, CancellationToken ct = default);
}
