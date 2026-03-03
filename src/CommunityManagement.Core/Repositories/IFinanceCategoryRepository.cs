using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IFinanceCategoryRepository
{
    Task<IReadOnlyList<FinanceCategory>> GetByOrgIdAsync(Guid orgId, string? type, bool? isActive, CancellationToken ct = default);
    Task<FinanceCategory?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(Guid orgId, string type, string name, Guid? excludeId, CancellationToken ct = default);
    Task<bool> HasRecordsAsync(Guid categoryId, CancellationToken ct = default);
    Task<bool> HasChildrenAsync(Guid categoryId, CancellationToken ct = default);
    Task<bool> HasCategoriesAsync(Guid orgId, CancellationToken ct = default);
    Task<FinanceCategory> CreateAsync(FinanceCategory category, CancellationToken ct = default);
    Task CreateBulkAsync(IReadOnlyList<FinanceCategory> categories, CancellationToken ct = default);
    Task UpdateAsync(FinanceCategory category, CancellationToken ct = default);
    Task DeactivateChildrenAsync(Guid parentId, CancellationToken ct = default);
}
