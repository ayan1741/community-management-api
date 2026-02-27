using CommunityManagement.Core.Enums;

namespace CommunityManagement.Core.Services;

public interface ICurrentUserService
{
    Guid UserId { get; }
    Task RequireRoleAsync(Guid orgId, MemberRole minimumRole, CancellationToken ct = default);
    Task<MemberStatus> GetMembershipStatusAsync(Guid orgId, CancellationToken ct = default);
}
