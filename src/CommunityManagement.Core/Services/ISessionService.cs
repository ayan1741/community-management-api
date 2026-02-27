namespace CommunityManagement.Core.Services;

public interface ISessionService
{
    Task RevokeAllAsync(Guid userId, CancellationToken ct = default);
    Task BanUserAsync(Guid userId, int days, CancellationToken ct = default);
    Task ChangeEmailAsync(Guid userId, string newEmail, CancellationToken ct = default);
    Task DeleteUserAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetBannedExpiredUsersAsync(CancellationToken ct = default);
}
