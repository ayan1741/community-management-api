using CommunityManagement.Core.Entities;
using System.Data;

namespace CommunityManagement.Core.Repositories;

public interface ILateFeeRepository
{
    Task<IReadOnlyList<LateFee>> GetByUnitDueIdAsync(Guid unitDueId, CancellationToken ct = default);
    Task<bool> HasActiveLateFeeAsync(Guid unitDueId, CancellationToken ct = default);
    Task<LateFee> CreateAsync(LateFee lateFee, CancellationToken ct = default);
    Task CancelAsync(Guid id, Guid cancelledBy, string note, CancellationToken ct = default);
    Task CancelByUnitDueIdAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default);
}
