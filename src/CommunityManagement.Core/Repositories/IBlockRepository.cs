using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IBlockRepository
{
    Task<IReadOnlyList<BlockListItem>> GetByOrgIdAsync(Guid orgId, CancellationToken ct = default);
    Task<Block?> GetByIdAsync(Guid blockId, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(Guid orgId, string name, Guid? excludeId, CancellationToken ct = default);
    Task<int> GetActiveUnitCountAsync(Guid blockId, CancellationToken ct = default);
    Task<Block> CreateAsync(Block block, CancellationToken ct = default);
    Task UpdateAsync(Block block, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid blockId, CancellationToken ct = default);
}

public record BlockListItem(
    Guid Id,
    string Name,
    string BlockType,
    bool IsDefault,
    int UnitCount,
    DateTimeOffset CreatedAt
);
