using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IDueTypeRepository
{
    Task<IReadOnlyList<DueType>> GetByOrgIdAsync(Guid orgId, bool? isActive, CancellationToken ct = default);
    Task<DueType?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(Guid orgId, string name, Guid? excludeId, CancellationToken ct = default);
    Task<bool> HasAccrualsAsync(Guid dueTypeId, CancellationToken ct = default);
    Task<DueType> CreateAsync(DueType dueType, CancellationToken ct = default);
    Task UpdateAsync(DueType dueType, CancellationToken ct = default);
}
