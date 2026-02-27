using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IUnitRepository
{
    Task<(IReadOnlyList<UnitListItem> Items, int TotalCount)> GetByOrgIdAsync(
        Guid orgId, Guid? blockId, string? unitType, bool? isOccupied,
        string? search, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<UnitDropdownItem>> GetDropdownByOrgIdAsync(Guid orgId, CancellationToken ct = default);
    Task<Unit?> GetByIdAsync(Guid unitId, CancellationToken ct = default);
    Task<bool> ExistsByNumberAsync(Guid blockId, string unitNumber, Guid? excludeId, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetExistingNumbersAsync(Guid blockId, IReadOnlyList<string> numbers, CancellationToken ct = default);
    Task<bool> HasActiveResidentAsync(Guid unitId, CancellationToken ct = default);
    Task<Unit> CreateAsync(Unit unit, CancellationToken ct = default);
    Task<IReadOnlyList<Unit>> CreateBulkAsync(IReadOnlyList<Unit> units, CancellationToken ct = default);
    Task UpdateAsync(Unit unit, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid unitId, CancellationToken ct = default);
}

public record UnitListItem(
    Guid Id,
    Guid BlockId,
    string BlockName,
    string UnitNumber,
    string UnitType,
    int? Floor,
    decimal? AreaSqm,
    bool IsOccupied,
    DateTimeOffset CreatedAt
);

public record UnitDropdownItem(
    Guid Id,
    Guid BlockId,
    string BlockName,
    string UnitNumber,
    bool IsOccupied
);
