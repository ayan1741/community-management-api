using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IUnitResidentRepository
{
    Task<IReadOnlyList<UnitResidentListItem>> GetByUnitIdAsync(Guid unitId, CancellationToken ct = default);
    Task<UnitResident?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsActiveAsync(Guid unitId, Guid userId, CancellationToken ct = default);
    Task<UnitResident?> CreateAsync(UnitResident resident, CancellationToken ct = default);
    Task RemoveAsync(Guid id, Guid removedBy, CancellationToken ct = default);
}

public record UnitResidentListItem(
    Guid Id,
    Guid UserId,
    string FullName,
    string? Phone,
    string ResidentType,
    bool IsPrimary,
    DateTimeOffset CreatedAt
);
