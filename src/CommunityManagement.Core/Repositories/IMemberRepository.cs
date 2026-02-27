using CommunityManagement.Core.Entities;
using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Repositories;

public interface IMemberRepository
{
    Task<OrganizationMember?> GetByUserIdAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<(IReadOnlyList<MemberListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, MemberStatus? status, MemberRole? role, int page, int pageSize, CancellationToken ct = default);
    Task<int> GetAdminCountAsync(Guid orgId, CancellationToken ct = default);
    Task UpdateRoleAsync(Guid orgId, Guid userId, MemberRole role, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid orgId, Guid userId, MemberStatus status, Guid? suspendedBy, CancellationToken ct = default);
    Task UpsertAsync(OrganizationMember member, CancellationToken ct = default);
    Task RemoveAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<MemberHistoryItem>> GetHistoryAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<bool> IsLastAdminAsync(Guid orgId, Guid userId, CancellationToken ct = default);
    Task<bool> IsLastAdminInAnyOrgAsync(Guid userId, CancellationToken ct = default);
}

public record MemberListItem(
    Guid UserId,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    string Role,
    string Status,
    IReadOnlyList<MemberUnitInfo> Units,
    DateTimeOffset JoinedAt
);

public record MemberUnitInfo(string UnitNumber, string BlockName);

public record MemberHistoryItem(
    string Action,
    string? ActorName,
    string? OldValue,
    string? NewValue,
    DateTimeOffset CreatedAt
);
