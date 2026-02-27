using CommunityManagement.Core.Entities;

namespace CommunityManagement.Core.Repositories;

public interface IOrganizationRepository
{
    Task<Organization> CreateAsync(Organization organization, CancellationToken ct = default);
    Task<Organization?> GetByIdAsync(Guid orgId, CancellationToken ct = default);
    Task<bool> ExistsForUserAsync(Guid userId, string name, CancellationToken ct = default);
}
