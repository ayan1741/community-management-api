using CommunityManagement.Core.Entities;
using System.Data;

namespace CommunityManagement.Core.Repositories;

public interface IUnitDueRepository
{
    Task<(IReadOnlyList<UnitDueListItem> Items, int TotalCount)> GetByPeriodIdAsync(
        Guid periodId, string? status, int page, int pageSize, CancellationToken ct = default);
    Task<IReadOnlyList<UnitDueResidentItem>> GetByUnitIdAsync(Guid unitId, Guid orgId, CancellationToken ct = default);
    Task<IReadOnlyList<UnitDueResidentItem>> GetByUserIdAsync(Guid userId, Guid orgId, CancellationToken ct = default);
    Task<UnitDue?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid periodId, Guid unitId, Guid dueTypeId, CancellationToken ct = default);
    Task<AccrualPreview> GetAccrualPreviewAsync(AccrualParams parameters, CancellationToken ct = default);
    Task BulkCreateAsync(IReadOnlyList<UnitDue> unitDues, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default);
    Task<UnitDue> CreateAsync(UnitDue unitDue, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid id, string status, CancellationToken ct = default);
    Task CancelWithLateFeesAsync(Guid unitDueId, Guid cancelledBy, IDbConnection conn, IDbTransaction tx, CancellationToken ct = default);
}

public record UnitDueListItem(
    Guid Id,
    Guid UnitId,
    string UnitNumber,
    string BlockName,
    string? ResidentName,
    string? UnitCategory,
    Guid DueTypeId,
    string DueTypeName,
    decimal Amount,
    decimal PaidAmount,
    decimal RemainingAmount,
    string Status,
    bool IsOverdue,
    DateTimeOffset CreatedAt
);

public record UnitDueResidentItem(
    Guid Id,
    string PeriodName,
    DateOnly DueDate,
    string DueTypeName,
    decimal Amount,
    decimal PaidAmount,
    string Status,
    bool IsOverdue,
    decimal? CalculatedLateFee,  // anlık hesaplama — DB'de saklanmaz
    DateTimeOffset CreatedAt
);

public record AccrualParams(
    Guid PeriodId,
    Guid OrganizationId,
    IReadOnlyList<Guid> DueTypeIds,
    bool IncludeEmptyUnits,
    Guid CreatedBy
);

public record AccrualPreview(
    int TotalUnits,
    int OccupiedUnits,
    int EmptyUnits,
    int IncludedUnits,
    IReadOnlyList<AccrualPreviewLine> DueTypeBreakdowns,
    int UnitsWithoutCategory,
    decimal TotalAmount
);

public record AccrualPreviewLine(
    Guid DueTypeId,
    string DueTypeName,
    IReadOnlyList<AccrualCategoryLine> CategoryLines,
    int UnitsWithoutCategory,
    decimal Subtotal
);

public record AccrualCategoryLine(string? Category, decimal Amount, int UnitCount, decimal Subtotal);
