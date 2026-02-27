using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IProfileRepository
{
    Task<UserProfile?> GetByIdAsync(Guid userId, CancellationToken ct = default);
    Task UpdateAsync(UserProfile profile, CancellationToken ct = default);
    Task RecordKvkkConsentAsync(Guid userId, DateTimeOffset consentAt, CancellationToken ct = default);
    Task MarkDeletionRequestedAsync(Guid userId, DateTimeOffset requestedAt, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<Guid>> GetDeletionRequestedExpiredAsync(DateTimeOffset before, CancellationToken ct = default);
    Task<MyContextResult> GetFullContextAsync(Guid userId, CancellationToken ct = default);
}

public record MyContextResult(
    UserProfile Profile,
    IReadOnlyList<MembershipContext> Memberships
);

public record MembershipContext(
    Guid OrganizationId,
    string OrganizationName,
    string Role,
    string Status,
    IReadOnlyList<UnitContext> Units
);

public record UnitContext(
    Guid UnitId,
    string UnitNumber,
    string BlockName,
    string ResidentType
);
